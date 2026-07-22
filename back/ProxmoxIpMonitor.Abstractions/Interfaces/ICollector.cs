using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Abstractions.Interfaces;

/// <summary>
///     The single owner of collection and DNS writes.
///     Everything that mutates the snapshot or the zone goes through this interface, which is
///     what keeps "one writer" enforceable rather than merely intended.
/// </summary>
public interface ICollector
{
	/// <summary>Runs one collection cycle now, waiting for any in-flight cycle to finish first.</summary>
	Task<IReadOnlyList<CollectionRun>> CollectNowAsync(CancellationToken ct = default);

	/// <summary>Reconciles DNS against the current snapshot without collecting first.</summary>
	Task<IReadOnlyList<DnsPush>> PushDnsAsync(CancellationToken ct = default);

	/// <summary>Computes the diff between the desired records and the live zone. Read-only.</summary>
	Task<IReadOnlyList<DnsState>> InspectDnsAsync(CancellationToken ct = default);
}
