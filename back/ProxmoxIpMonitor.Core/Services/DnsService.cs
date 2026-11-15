using Elyspio.Utils.Telemetry;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Core.Services;

public sealed class DnsService(
	ICollector collector,
	IDnsPushRepository pushes,
	ILogger<DnsService> logger) : IDnsService
{
	public async Task<IReadOnlyList<DnsState>> GetStateAsync(CancellationToken ct = default)
	{
		using var trace = logger.Trace();

		return await collector.InspectDnsAsync(ct);
	}

	public async Task<IReadOnlyList<DnsPush>> PushAsync(CancellationToken ct = default)
	{
		using var trace = logger.Trace();

		return await collector.PushDnsAsync(ct);
	}

	public async Task<IReadOnlyList<DnsPush>> GetRecentPushesAsync(int take, CancellationToken ct = default)
	{
		using var trace = logger.Trace($"{Log.F(take)}");

		return await pushes.GetRecentAsync(take, ct);
	}
}
