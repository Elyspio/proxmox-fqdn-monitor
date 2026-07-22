using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Interfaces;

namespace ProxmoxIpMonitor.Core.Services;

/// <summary>
///     Drives <see cref="Collector" /> on the configured interval.
///     The interval is re-read from settings between cycles rather than captured at startup,
///     so changing it in the UI takes effect without a restart.
/// </summary>
public sealed class CollectorHostedService(
	ICollector collector,
	ISettingsRepository settings,
	ILogger<CollectorHostedService> logger) : BackgroundService
{
	private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan FallbackInterval = TimeSpan.FromMinutes(1);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("Collector started");

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await collector.CollectNowAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception e)
			{
				// The loop is the only thing keeping the snapshot fresh; it must survive any
				// single failure, including a misconfigured database or DNS provider.
				logger.LogError(e, "Collection cycle failed");
			}

			var delay = await ResolveIntervalAsync(stoppingToken);

			try
			{
				await Task.Delay(delay, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}

		logger.LogInformation("Collector stopped");
	}

	private async Task<TimeSpan> ResolveIntervalAsync(CancellationToken ct)
	{
		try
		{
			var current = await settings.GetAsync(ct);
			// Floor the interval: a zero or negative value from the settings screen would turn
			// the loop into a hot spin against the Proxmox API.
			return current.PollInterval < MinimumInterval ? MinimumInterval : current.PollInterval;
		}
		catch (Exception e)
		{
			logger.LogError(e, "Could not read the poll interval; falling back to {Fallback}", FallbackInterval);
			return FallbackInterval;
		}
	}
}
