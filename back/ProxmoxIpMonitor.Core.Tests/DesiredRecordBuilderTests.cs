using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Core.Services;
using Xunit;

namespace ProxmoxIpMonitor.Core.Tests;

public class DesiredRecordBuilderTests
{
	private static readonly DateTime Now = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

	private static MonitoredHost Host(string hostname, string? ip, DateTime lastSeen, bool excluded = false, string nodeId = "node-1")
	{
		return new MonitoredHost
		{
			Key = MonitoredHost.BuildKey(nodeId, HostType.Vm, hostname.GetHashCode() & 0xFFFF),
			NodeId = nodeId,
			NodeName = nodeId,
			Type = HostType.Vm,
			VmId = 100,
			Hostname = hostname,
			Ip = ip,
			FirstSeenAt = lastSeen,
			LastSeenAt = lastSeen,
			Present = true,
			Excluded = excluded
		};
	}

	[Fact]
	public void HostsWithoutAnAddressAreNotWrittenToDns()
	{
		var records = DesiredRecordBuilder.Build([Host("web-01", null, Now)], new AppSettings());

		Assert.Empty(records);
	}

	[Fact]
	public void ExcludedHostsAreSkipped()
	{
		var hosts = new[] { Host("web-01", "10.0.10.5", Now, true), Host("web-02", "10.0.10.6", Now) };

		var records = DesiredRecordBuilder.Build(hosts, new AppSettings());

		Assert.Equal("web-02", Assert.Single(records).Domain);
	}

	[Fact]
	public void HostnamesInTheExclusionListAreSkippedCaseInsensitively()
	{
		var settings = new AppSettings { ExcludedHostnames = ["WEB-01"] };

		var records = DesiredRecordBuilder.Build([Host("web-01", "10.0.10.5", Now)], settings);

		Assert.Empty(records);
	}

	[Fact]
	public void DuplicateHostnamesResolveToTheMostRecentlySeenHost()
	{
		// The same name on two nodes (a migrated VM, or a stale entry) must not produce two
		// competing A records whose winner depends on iteration order.
		var hosts = new[]
		{
			Host("web-01", "10.0.10.5", Now.AddHours(-2), nodeId: "node-1"),
			Host("web-01", "10.0.10.9", Now, nodeId: "node-2")
		};

		var records = DesiredRecordBuilder.Build(hosts, new AppSettings());

		Assert.Equal("10.0.10.9", Assert.Single(records).Ip);
	}
}
