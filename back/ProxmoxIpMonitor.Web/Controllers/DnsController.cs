using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.AspNetCore.Mvc;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Web.Controllers;

[ApiController]
[Route("api/dns")]
public sealed class DnsController(IDnsService dns, ILogger<DnsController> logger) : TracingController(logger)
{
	/// <summary>
	///     Diff between the desired records and the live zone. Read-only: it never writes,
	///     which makes it safe to open before reconciliation has ever been enabled.
	/// </summary>
	[HttpGet("state")]
	public async Task<IReadOnlyList<DnsState>> GetState(CancellationToken ct)
	{
		using var trace = LogController();

		return await dns.GetStateAsync(ct);
	}

	/// <summary>
	///     Applies the diff now. Still a dry run when reconciliation is disabled in settings —
	///     the button never silently overrides that switch.
	/// </summary>
	[HttpPost("push")]
	public async Task<IReadOnlyList<DnsPush>> Push(CancellationToken ct)
	{
		using var trace = LogController();

		return await dns.PushAsync(ct);
	}

	[HttpGet("pushes")]
	public async Task<IReadOnlyList<DnsPush>> GetPushes([FromQuery] int take = 20, CancellationToken ct = default)
	{
		using var trace = LogController($"{Log.F(take)}");

		return await dns.GetRecentPushesAsync(take, ct);
	}
}
