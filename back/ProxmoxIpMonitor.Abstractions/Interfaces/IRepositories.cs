using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Abstractions.Interfaces;

/// <summary>Filters applied to the history screen.</summary>
public sealed record IpEventQuery
{
	public string? HostKey { get; init; }

	public IpEventKind? Kind { get; init; }

	public int Skip { get; init; }

	public int Take { get; init; } = 100;
}

public sealed record Page<T>(IReadOnlyList<T> Items, long Total);

public interface ISettingsRepository
{
	/// <summary>Returns the stored settings, creating the defaults on first run.</summary>
	Task<AppSettings> GetAsync(CancellationToken ct = default);

	Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken ct = default);
}

public interface INodeRepository
{
	Task<IReadOnlyList<PveNode>> GetAllAsync(CancellationToken ct = default);

	Task<PveNode?> GetAsync(string id, CancellationToken ct = default);

	Task<PveNode> CreateAsync(PveNode node, CancellationToken ct = default);

	Task<PveNode> UpdateAsync(PveNode node, CancellationToken ct = default);

	Task DeleteAsync(string id, CancellationToken ct = default);
}

public interface IHostRepository
{
	Task<IReadOnlyList<MonitoredHost>> GetAllAsync(CancellationToken ct = default);

	Task<MonitoredHost?> GetAsync(string key, CancellationToken ct = default);

	/// <summary>Replaces the stored snapshot for the given hosts, inserting or updating by key.</summary>
	Task UpsertManyAsync(IReadOnlyCollection<MonitoredHost> hosts, CancellationToken ct = default);

	Task DeleteManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);

	/// <summary>Removes every host belonging to a node that no longer exists.</summary>
	Task DeleteByNodeAsync(string nodeId, CancellationToken ct = default);
}

public interface IIpEventRepository
{
	Task AppendManyAsync(IReadOnlyCollection<IpEvent> events, CancellationToken ct = default);

	Task<Page<IpEvent>> QueryAsync(IpEventQuery query, CancellationToken ct = default);
}

public interface ICollectionRunRepository
{
	Task AppendAsync(CollectionRun run, CancellationToken ct = default);

	/// <summary>Most recent run per node, for the health screen.</summary>
	Task<IReadOnlyList<CollectionRun>> GetLatestPerNodeAsync(CancellationToken ct = default);

	Task<IReadOnlyList<CollectionRun>> GetRecentAsync(string nodeId, int take, CancellationToken ct = default);
}

public interface IDnsPushRepository
{
	Task AppendAsync(DnsPush push, CancellationToken ct = default);

	Task<IReadOnlyList<DnsPush>> GetRecentAsync(int take, CancellationToken ct = default);
}
