using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Repositories;

public sealed class HostRepository(MongoContext context, ILogger<HostRepository> logger) : TracingRepository(logger), IHostRepository
{
	public async Task<IReadOnlyList<MonitoredHost>> GetAllAsync(CancellationToken ct = default)
	{
		using var trace = LogRepository();

		return await context.Hosts.Find(FilterDefinition<MonitoredHost>.Empty)
			.SortBy(host => host.Hostname)
			.ToListAsync(ct);
	}

	public async Task<MonitoredHost?> GetAsync(string key, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(key)}");

		return await context.Hosts.Find(host => host.Key == key).FirstOrDefaultAsync(ct);
	}

	public async Task UpsertManyAsync(IReadOnlyCollection<MonitoredHost> hosts, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(hosts.Count)}");

		if (hosts.Count == 0) return;

		var writes = hosts.Select(host => new ReplaceOneModel<MonitoredHost>(
			Builders<MonitoredHost>.Filter.Eq(stored => stored.Key, host.Key), host)
		{
			IsUpsert = true
		});

		await context.Hosts.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
	}

	public async Task DeleteManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(keys.Count)}");

		if (keys.Count == 0) return;
		await context.Hosts.DeleteManyAsync(host => keys.Contains(host.Key), ct);
	}

	public async Task DeleteByNodeAsync(string nodeId, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(nodeId)}");

		await context.Hosts.DeleteManyAsync(host => host.NodeId == nodeId, ct);
	}
}
