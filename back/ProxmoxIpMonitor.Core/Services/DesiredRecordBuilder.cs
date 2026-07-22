using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Core.Services;

/// <summary>
///     Turns the stored snapshot into the set of A records the zone should hold.
///     Separated from the collector so the exclusion and de-duplication rules are testable
///     without touching Mongo or the DNS provider.
/// </summary>
public static class DesiredRecordBuilder
{
	public static IReadOnlyList<DesiredRecord> Build(IEnumerable<MonitoredHost> hosts, AppSettings settings)
	{
		var excluded = settings.ExcludedHostnames.ToHashSet(StringComparer.OrdinalIgnoreCase);

		return hosts
			.Where(host => !host.Excluded)
			.Where(host => !excluded.Contains(host.Hostname))
			.Where(host => !string.IsNullOrWhiteSpace(host.Ip))
			// Two guests can legitimately carry the same name across nodes (a migrated VM seen
			// twice, a stale entry). Prefer the one seen most recently rather than writing both
			// and letting the zone end up with a non-deterministic winner.
			.GroupBy(host => host.Hostname, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.OrderByDescending(host => host.LastSeenAt).First())
			.Select(host => new DesiredRecord(host.Hostname, host.Ip!))
			.OrderBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}
}
