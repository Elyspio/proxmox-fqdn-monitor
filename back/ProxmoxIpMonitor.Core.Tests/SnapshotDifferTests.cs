using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Core.Services;
using Shouldly;
using Xunit;

namespace ProxmoxIpMonitor.Core.Tests;

public class SnapshotDifferTests
{
	private const string NodeId = "node-1";
	private static readonly DateTime Now = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
	private static readonly TimeSpan Retention = TimeSpan.FromMinutes(300);

	private static readonly Dictionary<string, string> NodeNames = new() { [NodeId] = "proxmox" };

	private static MonitoredHost Stored(string hostname, string? ip, int vmId, DateTime lastSeen, bool present = true, bool pinned = false, int? vlan = null)
	{
		return new MonitoredHost
		{
			Key = MonitoredHost.BuildKey(NodeId, HostType.Container, vmId),
			NodeId = NodeId,
			NodeName = "proxmox",
			Type = HostType.Container,
			VmId = vmId,
			Hostname = hostname,
			Ip = ip,
			Vlan = vlan,
			FirstSeenAt = lastSeen,
			LastSeenAt = lastSeen,
			Present = present,
			Pinned = pinned
		};
	}

	private static DiffResult Diff(
		IReadOnlyCollection<MonitoredHost> stored,
		IReadOnlyList<DiscoveredHost> discovered,
		bool nodePolled = true)
	{
		return SnapshotDiffer.Diff(
			stored,
			nodePolled ? new Dictionary<string, IReadOnlyList<DiscoveredHost>> { [NodeId] = discovered } : new Dictionary<string, IReadOnlyList<DiscoveredHost>>(),
			nodePolled ? [NodeId] : [],
			NodeNames,
			Retention,
			Now);
	}

	private static DiscoveredHost Found(string hostname, string? ip, int vmId, string? issue = null, int? vlan = null)
	{
		return new DiscoveredHost { Type = HostType.Container, VmId = vmId, Hostname = hostname, Ip = ip, Vlan = vlan, Issue = issue };
	}

	[Fact]
	public void NewHostWithAddressIsRecordedAsAppeared()
	{
		var result = Diff([], [Found("web-01", "10.0.10.5", 101)]);

		var host = result.Upserts.ShouldHaveSingleItem();
		host.Ip.ShouldBe("10.0.10.5");
		host.Present.ShouldBeTrue();

		var evt = result.Events.ShouldHaveSingleItem();
		evt.Kind.ShouldBe(IpEventKind.Appeared);
		evt.PreviousIp.ShouldBeNull();
		evt.CurrentIp.ShouldBe("10.0.10.5");
	}

	[Fact]
	public void ChangedAddressProducesChangedEventCarryingBothValues()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-01", "10.0.10.9", 101)]);

		var evt = result.Events.ShouldHaveSingleItem();
		evt.Kind.ShouldBe(IpEventKind.Changed);
		evt.PreviousIp.ShouldBe("10.0.10.5");
		evt.CurrentIp.ShouldBe("10.0.10.9");
	}

	[Fact]
	public void UnchangedAddressProducesNoEvent()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-01", "10.0.10.5", 101)]);

		result.Events.ShouldBeEmpty();
		result.Deletions.ShouldBeEmpty();
	}

	[Fact]
	public void HostInsideRetentionKeepsItsAddressWhenItStopsReporting()
	{
		// A rebooting guest must not lose its DNS record: this is the behaviour the original
		// exporter's FqdnRetentionMinutes existed for.
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-10));

		var result = Diff([stored], []);

		result.Deletions.ShouldBeEmpty();
		var kept = result.Upserts.ShouldHaveSingleItem();
		kept.Ip.ShouldBe("10.0.10.5");
		kept.Present.ShouldBeFalse();
		result.Events.ShouldHaveSingleItem().Kind.ShouldBe(IpEventKind.Disappeared);
	}

	[Fact]
	public void HostPastRetentionIsDeleted()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-301));

		var result = Diff([stored], []);

		result.Deletions.ShouldHaveSingleItem().ShouldBe(stored.Key);
	}

	[Fact]
	public void PinnedHostSurvivesRetentionExpiry()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5000), pinned: true);

		var result = Diff([stored], []);

		result.Deletions.ShouldBeEmpty();
	}

	[Fact]
	public void HostsOfAFailedNodeAreLeftAloneEvenPastRetention()
	{
		// A node that could not be polled says nothing about its guests. Expiring them would turn
		// a transient API outage into a mass DNS deletion.
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5000));

		var result = Diff([stored], [], false);

		result.Deletions.ShouldBeEmpty();
		result.Upserts.ShouldBeEmpty();
		result.Events.ShouldBeEmpty();
	}

	[Fact]
	public void GuestReportingNoAddressKeepsItsStoredOne()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-01", null, 101, "QEMU guest agent is not responding")]);

		var host = result.Upserts.ShouldHaveSingleItem();
		host.Ip.ShouldBe("10.0.10.5");
		host.Present.ShouldBeFalse();
		result.Events.ShouldHaveSingleItem().Kind.ShouldBe(IpEventKind.Disappeared);
	}

	[Fact]
	public void DiscoveredVlanIsRecordedAndReplacesTheStoredOne()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5), vlan: 10);

		var result = Diff([stored], [Found("web-01", "10.0.10.5", 101, vlan: 30)]);

		result.Upserts.ShouldHaveSingleItem().Vlan.ShouldBe(30);
	}

	[Fact]
	public void VlanIsRetainedWhenAHostStopsReporting()
	{
		// The VLAN belongs to the retained address; a rebooting guest must not lose it.
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-10), vlan: 30);

		var result = Diff([stored], []);

		var kept = result.Upserts.ShouldHaveSingleItem();
		kept.Ip.ShouldBe("10.0.10.5");
		kept.Vlan.ShouldBe(30);
	}

	[Fact]
	public void HostComingBackAfterDropoutIsReportedAsAppeared()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-10), false);

		var result = Diff([stored], [Found("web-01", "10.0.10.5", 101)]);

		var evt = result.Events.ShouldHaveSingleItem();
		evt.Kind.ShouldBe(IpEventKind.Appeared);
		result.Upserts.ShouldHaveSingleItem().Present.ShouldBeTrue();
	}

	[Fact]
	public void RenamingAGuestIsDistinctFromReaddressingIt()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-02", "10.0.10.5", 101)]);

		var evt = result.Events.ShouldHaveSingleItem();
		evt.Kind.ShouldBe(IpEventKind.Renamed);
	}

	[Fact]
	public void MissingAddressOnAnAlreadyAbsentHostDoesNotRepeatTheEvent()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-10), false);

		var result = Diff([stored], [Found("web-01", null, 101, "no agent")]);

		result.Events.ShouldBeEmpty();
	}
}
