using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.MongoDB.Mongo;

namespace ProxmoxIpMonitor.Adapters.MongoDB.Repositories;

public sealed class NodeRepository(MongoContext context, ILogger<NodeRepository> logger) : TracingRepository(logger), INodeRepository
{
	public async Task<IReadOnlyList<PveNode>> GetAllAsync(CancellationToken ct = default)
	{
		using var trace = LogRepository();

		return await context.Nodes.Find(FilterDefinition<PveNode>.Empty)
			.SortBy(node => node.DisplayName)
			.ToListAsync(ct);
	}

	public async Task<PveNode?> GetAsync(string id, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(id)}");

		return await context.Nodes.Find(node => node.Id == id).FirstOrDefaultAsync(ct);
	}

	public async Task<PveNode> CreateAsync(PveNode node, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(node.DisplayName)}");

		var toInsert = node with { Id = null };
		await context.Nodes.InsertOneAsync(toInsert, cancellationToken: ct);
		return toInsert;
	}

	public async Task<PveNode> UpdateAsync(PveNode node, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(node.Id)} {Log.F(node.DisplayName)}");

		await context.Nodes.ReplaceOneAsync(stored => stored.Id == node.Id, node, cancellationToken: ct);
		return node;
	}

	public async Task DeleteAsync(string id, CancellationToken ct = default)
	{
		using var trace = LogRepository($"{Log.F(id)}");

		await context.Nodes.DeleteOneAsync(node => node.Id == id, ct);
	}
}
