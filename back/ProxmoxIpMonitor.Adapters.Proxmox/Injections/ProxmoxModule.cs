using Microsoft.Extensions.DependencyInjection;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Adapters.Proxmox.Pve;

namespace ProxmoxIpMonitor.Adapters.Proxmox.Injections;

public static class ProxmoxModule
{
	public static IServiceCollection AddProxmoxAdapter(this IServiceCollection services)
	{
		services.AddSingleton<PveHttpClientProvider>();
		services.AddSingleton<IPveHttpClientProvider>(sp => sp.GetRequiredService<PveHttpClientProvider>());
		services.AddSingleton<IPveClient, PveApiClient>();
		return services;
	}
}
