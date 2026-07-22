using Microsoft.Extensions.DependencyInjection;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Core.Services;

namespace ProxmoxIpMonitor.Core.Injections;

public static class CoreModule
{
	public static IServiceCollection AddCore(this IServiceCollection services)
	{
		// Singleton on purpose: the collector owns the only write path to the snapshot and the
		// DNS zone, and its serialising semaphore only means anything if there is one instance.
		services.AddSingleton<ICollector, Collector>();
		services.AddHostedService<CollectorHostedService>();

		// The API-facing services controllers are required to go through.
		services.AddScoped<INodeService, NodeService>();
		services.AddScoped<IHostService, HostService>();
		services.AddScoped<ISettingsService, SettingsService>();
		services.AddScoped<IDnsService, DnsService>();

		return services;
	}
}
