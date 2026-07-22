using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Repositories;

/// <summary>Append-only history of address changes. Bounded by the TTL index, never updated.</summary>
public sealed class IpEventRepository(MongoContext context, ILogger<IpEventRepository> logger) : TracingRepository(logger), IIpEventRepository
{
	public async Task AppendManyAsync(IReadOnlyCollection<IpEvent> events, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(events.Count)}");

		if (events.Count == 0) return;
		await context.IpEvents.InsertManyAsync(events, new InsertManyOptions { IsOrdered = false }, ct);
	}

	public async Task<Page<IpEvent>> QueryAsync(IpEventQuery query, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(query)}");

		var builder = Builders<IpEvent>.Filter;
		var filter = builder.Empty;

		if (!string.IsNullOrWhiteSpace(query.HostKey))
			filter &= builder.Eq(evt => evt.HostKey, query.HostKey);

		if (query.Kind is { } kind)
			filter &= builder.Eq(evt => evt.Kind, kind);

		var total = await context.IpEvents.CountDocumentsAsync(filter, cancellationToken: ct);

		var items = await context.IpEvents.Find(filter)
			.SortByDescending(evt => evt.At)
			.Skip(Math.Max(0, query.Skip))
			.Limit(Math.Clamp(query.Take, 1, 500))
			.ToListAsync(ct);

		return new Page<IpEvent>(items, total);
	}
}

public sealed class CollectionRunRepository(MongoContext context, ILogger<CollectionRunRepository> logger) : TracingRepository(logger), ICollectionRunRepository
{
	public async Task AppendAsync(CollectionRun run, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(run.NodeName)}");

		await context.CollectionRuns.InsertOneAsync(run, cancellationToken: ct);
	}

	public async Task<IReadOnlyList<CollectionRun>> GetLatestPerNodeAsync(CancellationToken ct = default)
	{
		using var trace = LogRepository();

		// One document per node, newest first. Small collection after TTL expiry, so a sort
		// plus in-memory grouping beats an aggregation pipeline in readability here.
		var recent = await context.CollectionRuns.Find(FilterDefinition<CollectionRun>.Empty)
			.SortByDescending(run => run.StartedAt)
			.Limit(500)
			.ToListAsync(ct);

		return recent.GroupBy(run => run.NodeId).Select(group => group.First()).ToList();
	}

	public async Task<IReadOnlyList<CollectionRun>> GetRecentAsync(string nodeId, int take, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(nodeId)} {Log.F(take)}");

		return await context.CollectionRuns.Find(run => run.NodeId == nodeId)
			.SortByDescending(run => run.StartedAt)
			.Limit(Math.Clamp(take, 1, 200))
			.ToListAsync(ct);
	}
}

public sealed class DnsPushRepository(MongoContext context, ILogger<DnsPushRepository> logger) : TracingRepository(logger), IDnsPushRepository
{
	public async Task AppendAsync(DnsPush push, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(push.DryRun)}");

		await context.DnsPushes.InsertOneAsync(push, cancellationToken: ct);
	}

	public async Task<IReadOnlyList<DnsPush>> GetRecentAsync(int take, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(take)}");

		return await context.DnsPushes.Find(FilterDefinition<DnsPush>.Empty)
			.SortByDescending(push => push.At)
			.Limit(Math.Clamp(take, 1, 200))
			.ToListAsync(ct);
	}
}
