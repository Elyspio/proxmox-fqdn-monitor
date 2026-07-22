using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Transports;

namespace ProxmoxIpMonitor.Abstractions.Interfaces;

/// <summary>
///     Node configuration lifecycle. Owns the token-protection rules: raw secrets enter here,
///     only protected values ever reach the repository, and "empty secret means unchanged".
/// </summary>
public interface INodeService
{
	Task<IReadOnlyList<NodeDto>> GetAllAsync(CancellationToken ct = default);

	Task<NodeDto> CreateAsync(NodeWriteDto node, CancellationToken ct = default);

	Task<NodeDto> UpdateAsync(string id, NodeWriteDto node, CancellationToken ct = default);

	/// <summary>Deletes the node and every host it owns, so no orphan lingers in the snapshot.</summary>
	Task DeleteAsync(string id, CancellationToken ct = default);

	/// <summary>Verifies reachability, TLS and token, optionally reusing the stored secret of <paramref name="id" />.</summary>
	Task<NodeTestResultDto> TestAsync(NodeWriteDto node, string? id, CancellationToken ct = default);
}

/// <summary>Read side of the snapshot plus the per-host flags the collector honours.</summary>
public interface IHostService
{
	Task<IReadOnlyList<MonitoredHost>> GetAllAsync(CancellationToken ct = default);

	Task<MonitoredHost> PatchAsync(string key, HostPatchDto patch, CancellationToken ct = default);

	Task<Page<IpEvent>> QueryEventsAsync(IpEventQuery query, CancellationToken ct = default);

	Task<IReadOnlyList<CollectionRun>> GetLatestRunsAsync(CancellationToken ct = default);

	Task<IReadOnlyList<CollectionRun>> GetRunHistoryAsync(string nodeId, int take, CancellationToken ct = default);
}

/// <summary>Application settings, including the Technitium token protection rules.</summary>
public interface ISettingsService
{
	Task<SettingsDto> GetAsync(CancellationToken ct = default);

	Task<SettingsDto> UpdateAsync(SettingsWriteDto settings, CancellationToken ct = default);
}

/// <summary>DNS reconciliation as exposed to the API: diff, push, and push history.</summary>
public interface IDnsService
{
	Task<IReadOnlyList<DnsState>> GetStateAsync(CancellationToken ct = default);

	Task<IReadOnlyList<DnsPush>> PushAsync(CancellationToken ct = default);

	Task<IReadOnlyList<DnsPush>> GetRecentPushesAsync(int take, CancellationToken ct = default);
}
