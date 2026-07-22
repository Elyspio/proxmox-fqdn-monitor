using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Core.Services;
using Xunit;

namespace ProxmoxIpMonitor.Core.Tests;

public class SnapshotDifferTests
{
	private const string NodeId = "node-1";
	private static readonly DateTime Now = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
	private static readonly TimeSpan Retention = TimeSpan.FromMinutes(300);

	private static readonly Dictionary<string, string> NodeNames = new() { [NodeId] = "proxmox" };

	private static MonitoredHost Stored(string hostname, string? ip, int vmId, DateTime lastSeen, bool present = true, bool pinned = false)
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

	private static DiscoveredHost Found(string hostname, string? ip, int vmId, string? issue = null)
	{
		return new DiscoveredHost { Type = HostType.Container, VmId = vmId, Hostname = hostname, Ip = ip, Issue = issue };
	}

	[Fact]
	public void NewHostWithAddressIsRecordedAsAppeared()
	{
		var result = Diff([], [Found("web-01", "10.0.10.5", 101)]);

		var host = Assert.Single(result.Upserts);
		Assert.Equal("10.0.10.5", host.Ip);
		Assert.True(host.Present);

		var evt = Assert.Single(result.Events);
		Assert.Equal(IpEventKind.Appeared, evt.Kind);
		Assert.Null(evt.PreviousIp);
		Assert.Equal("10.0.10.5", evt.CurrentIp);
	}

	[Fact]
	public void ChangedAddressProducesChangedEventCarryingBothValues()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-01", "10.0.10.9", 101)]);

		var evt = Assert.Single(result.Events);
		Assert.Equal(IpEventKind.Changed, evt.Kind);
		Assert.Equal("10.0.10.5", evt.PreviousIp);
		Assert.Equal("10.0.10.9", evt.CurrentIp);
	}

	[Fact]
	public void UnchangedAddressProducesNoEvent()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-01", "10.0.10.5", 101)]);

		Assert.Empty(result.Events);
		Assert.Empty(result.Deletions);
	}

	[Fact]
	public void HostInsideRetentionKeepsItsAddressWhenItStopsReporting()
	{
		// A rebooting guest must not lose its DNS record: this is the behaviour the original
		// exporter's FqdnRetentionMinutes existed for.
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-10));

		var result = Diff([stored], []);

		Assert.Empty(result.Deletions);
		var kept = Assert.Single(result.Upserts);
		Assert.Equal("10.0.10.5", kept.Ip);
		Assert.False(kept.Present);
		Assert.Equal(IpEventKind.Disappeared, Assert.Single(result.Events).Kind);
	}

	[Fact]
	public void HostPastRetentionIsDeleted()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-301));

		var result = Diff([stored], []);

		Assert.Equal(stored.Key, Assert.Single(result.Deletions));
	}

	[Fact]
	public void PinnedHostSurvivesRetentionExpiry()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5000), pinned: true);

		var result = Diff([stored], []);

		Assert.Empty(result.Deletions);
	}

	[Fact]
	public void HostsOfAFailedNodeAreLeftAloneEvenPastRetention()
	{
		// A node that could not be polled says nothing about its guests. Expiring them would turn
		// a transient API outage into a mass DNS deletion.
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5000));

		var result = Diff([stored], [], false);

		Assert.Empty(result.Deletions);
		Assert.Empty(result.Upserts);
		Assert.Empty(result.Events);
	}

	[Fact]
	public void GuestReportingNoAddressKeepsItsStoredOne()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-01", null, 101, "QEMU guest agent is not responding")]);

		var host = Assert.Single(result.Upserts);
		Assert.Equal("10.0.10.5", host.Ip);
		Assert.False(host.Present);
		Assert.Equal(IpEventKind.Disappeared, Assert.Single(result.Events).Kind);
	}

	[Fact]
	public void HostComingBackAfterDropoutIsReportedAsAppeared()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-10), false);

		var result = Diff([stored], [Found("web-01", "10.0.10.5", 101)]);

		var evt = Assert.Single(result.Events);
		Assert.Equal(IpEventKind.Appeared, evt.Kind);
		Assert.True(Assert.Single(result.Upserts).Present);
	}

	[Fact]
	public void RenamingAGuestIsDistinctFromReaddressingIt()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-5));

		var result = Diff([stored], [Found("web-02", "10.0.10.5", 101)]);

		var evt = Assert.Single(result.Events);
		Assert.Equal(IpEventKind.Renamed, evt.Kind);
	}

	[Fact]
	public void MissingAddressOnAnAlreadyAbsentHostDoesNotRepeatTheEvent()
	{
		var stored = Stored("web-01", "10.0.10.5", 101, Now.AddMinutes(-10), false);

		var result = Diff([stored], [Found("web-01", null, 101, "no agent")]);

		Assert.Empty(result.Events);
	}
}
