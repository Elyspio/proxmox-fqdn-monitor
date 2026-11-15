using Elyspio.Utils.Results;
using Elyspio.Utils.Telemetry;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Errors;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Transports;

namespace ProxmoxIpMonitor.Core.Services;

public sealed class HostService(
	IHostRepository hosts,
	IIpEventRepository events,
	ICollectionRunRepository runs,
	ILogger<HostService> logger) : IHostService
{
	public async Task<IReadOnlyList<MonitoredHost>> GetAllAsync(CancellationToken ct = default)
	{
		using var trace = logger.Trace();

		return await hosts.GetAllAsync(ct);
	}

	public async Task<Result<MonitoredHost>> PatchAsync(string key, HostPatchDto patch, CancellationToken ct = default)
	{
		using var trace = logger.Trace($"{Log.F(key)} {Log.F(patch)}");

		var host = await hosts.GetAsync(key, ct);
		if (host is null) return trace.Fail(new ResourceNotFound($"Unknown host '{key}'."));

		var updated = host with
		{
			Pinned = patch.Pinned ?? host.Pinned,
			Excluded = patch.Excluded ?? host.Excluded
		};

		await hosts.UpsertManyAsync([updated], ct);
		return updated;
	}

	public async Task<Page<IpEvent>> QueryEventsAsync(IpEventQuery query, CancellationToken ct = default)
	{
		using var trace = logger.Trace($"{Log.F(query)}");

		return await events.QueryAsync(query, ct);
	}

	public async Task<IReadOnlyList<CollectionRun>> GetLatestRunsAsync(CancellationToken ct = default)
	{
		using var trace = logger.Trace();

		return await runs.GetLatestPerNodeAsync(ct);
	}

	public async Task<IReadOnlyList<CollectionRun>> GetRunHistoryAsync(string nodeId, int take, CancellationToken ct = default)
	{
		using var trace = logger.Trace($"{Log.F(nodeId)} {Log.F(take)}");

		return await runs.GetRecentAsync(nodeId, take, ct);
	}
}
