using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Core.Services;

public sealed class DnsService(
	ICollector collector,
	IDnsPushRepository pushes,
	ILogger<DnsService> logger) : TracingService(logger), IDnsService
{
	public async Task<IReadOnlyList<DnsState>> GetStateAsync(CancellationToken ct = default)
	{
		using var trace = LogService();

		return await collector.InspectDnsAsync(ct);
	}

	public async Task<IReadOnlyList<DnsPush>> PushAsync(CancellationToken ct = default)
	{
		using var trace = LogService();

		return await collector.PushDnsAsync(ct);
	}

	public async Task<IReadOnlyList<DnsPush>> GetRecentPushesAsync(int take, CancellationToken ct = default)
	{
		using var trace = LogService($"{Log.F(take)}");

		return await pushes.GetRecentAsync(take, ct);
	}
}
