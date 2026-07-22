using Microsoft.Extensions.DependencyInjection;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Adapters.Dns.Technitium;

namespace ProxmoxIpMonitor.Adapters.Dns.Injections;

public static class DnsModule
{
	public static IServiceCollection AddDnsAdapters(this IServiceCollection services)
	{
		services.AddHttpClient(nameof(TechnitiumDnsProvider), client => client.Timeout = TimeSpan.FromSeconds(30));
		services.AddSingleton<IDnsProvider, TechnitiumDnsProvider>();
		return services;
	}
}
