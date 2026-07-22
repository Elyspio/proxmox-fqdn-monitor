using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Abstractions.Interfaces;

/// <summary>
///     Reconciles a DNS zone against the set of records the backend wants it to hold.
///     Kept as an interface from the original exporter so a second provider can be added
///     without touching the collector.
/// </summary>
public interface IDnsProvider
{
	/// <summary>Provider name used in logs and in the push journal.</summary>
	string Name { get; }

	/// <summary>Whether the current settings enable this provider.</summary>
	bool IsEnabled(AppSettings settings);

	/// <summary>Computes the diff between desired records and the live zone, without writing.</summary>
	Task<DnsState> InspectAsync(AppSettings settings, IReadOnlyCollection<DesiredRecord> desired, CancellationToken ct = default);

	/// <summary>
	///     Applies the diff. Writes only records this tool owns, and deletes only records that
	///     carry its ownership marker — records created by hand are structurally out of reach.
	/// </summary>
	Task<DnsPush> ReconcileAsync(AppSettings settings, IReadOnlyCollection<DesiredRecord> desired, bool dryRun, CancellationToken ct = default);
}
