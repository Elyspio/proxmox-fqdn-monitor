using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.AspNetCore.Mvc;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Transports;

namespace ProxmoxIpMonitor.Web.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(ISettingsService settings, ILogger<SettingsController> logger) : TracingController(logger)
{
	[HttpGet]
	public async Task<SettingsDto> Get(CancellationToken ct)
	{
		using var trace = LogController();

		return await settings.GetAsync(ct);
	}

	[HttpPut]
	public async Task<SettingsDto> Update([FromBody] SettingsWriteDto body, CancellationToken ct)
	{
		// The body carries the raw Technitium token: nothing from it goes through Log.F.
		using var trace = LogController();

		return await settings.UpdateAsync(body, ct);
	}
}
