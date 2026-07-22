using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.AspNetCore.Mvc;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Transports;

namespace ProxmoxIpMonitor.Web.Controllers;

[ApiController]
[Route("api/hosts")]
public sealed class HostsController(IHostService hosts, ILogger<HostsController> logger) : TracingController(logger)
{
	/// <summary>Current snapshot across every configured node.</summary>
	[HttpGet]
	public async Task<IReadOnlyList<MonitoredHost>> GetAll(CancellationToken ct)
	{
		using var trace = LogController();

		return await hosts.GetAllAsync(ct);
	}

	/// <summary>Toggles the per-host flags the collector honours: pinning and DNS exclusion.</summary>
	[HttpPatch("{key}")]
	public async Task<MonitoredHost> Patch(string key, [FromBody] HostPatchDto patch, CancellationToken ct)
	{
		using var trace = LogController($"{Log.F(key)} {Log.F(patch)}");

		return await hosts.PatchAsync(key, patch, ct);
	}
}

[ApiController]
[Route("api/events")]
public sealed class EventsController(IHostService hosts, ILogger<EventsController> logger) : TracingController(logger)
{
	/// <summary>Address-change history, newest first.</summary>
	[HttpGet]
	public async Task<Page<IpEvent>> Query(
		[FromQuery] string? hostKey,
		[FromQuery] IpEventKind? kind,
		[FromQuery] int skip = 0,
		[FromQuery] int take = 100,
		CancellationToken ct = default)
	{
		using var trace = LogController($"{Log.F(hostKey)} {Log.F(kind)} {Log.F(skip)} {Log.F(take)}");

		return await hosts.QueryEventsAsync(new IpEventQuery { HostKey = hostKey, Kind = kind, Skip = skip, Take = take }, ct);
	}
}

[ApiController]
[Route("api/health")]
public sealed class CollectionHealthController(IHostService hosts, ILogger<CollectionHealthController> logger) : TracingController(logger)
{
	/// <summary>Latest collection outcome per node — what the health screen reads.</summary>
	[HttpGet("collection")]
	public async Task<IReadOnlyList<CollectionRun>> GetLatest(CancellationToken ct)
	{
		using var trace = LogController();

		return await hosts.GetLatestRunsAsync(ct);
	}

	[HttpGet("collection/{nodeId}")]
	public async Task<IReadOnlyList<CollectionRun>> GetHistory(string nodeId, [FromQuery] int take = 20, CancellationToken ct = default)
	{
		using var trace = LogController($"{Log.F(nodeId)} {Log.F(take)}");

		return await hosts.GetRunHistoryAsync(nodeId, take, ct);
	}
}

[ApiController]
[Route("api/collect")]
public sealed class CollectController(ICollector collector, ILogger<CollectController> logger) : TracingController(logger)
{
	/// <summary>Forces a collection cycle. Waits for any in-flight cycle rather than running beside it.</summary>
	[HttpPost]
	public async Task<IReadOnlyList<CollectionRun>> Collect(CancellationToken ct)
	{
		using var trace = LogController();

		return await collector.CollectNowAsync(ct);
	}
}
