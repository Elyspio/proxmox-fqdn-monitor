using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Proxmox.Fqdn.Exporter.Abstractions.Interfaces.Adapters;
using Proxmox.Fqdn.Exporter.Abstractions.Interfaces.Services;
using Proxmox.Fqdn.Exporter.Adapters.Proxmox;
using Proxmox.Fqdn.Exporter.Data;
using Proxmox.Fqdn.Exporter.Options;

namespace Proxmox.Fqdn.Exporter.Services;

/// <summary>
///     Legacy DNS provider that writes a Pi-hole custom hosts file and reloads Pi-hole DNS.
/// </summary>
public class PiholeService : IDnsProvider
{
	private readonly IOptionsMonitor<AppConfig> _config;
	private readonly ContainerProxmoxAdapter _containerProxmoxAdapter;
	private readonly FqdnService _fqdnService;
	private readonly ILogger<PiholeService> _logger;
	private readonly VmProxmoxAdapter _vmProxmoxAdapter;

	public PiholeService(ILogger<PiholeService> logger, IOptionsMonitor<AppConfig> config, VmProxmoxAdapter vmProxmoxAdapter, ContainerProxmoxAdapter containerProxmoxAdapter, FqdnService fqdnService)
	{
		_logger = logger;
		_config = config;
		_vmProxmoxAdapter = vmProxmoxAdapter;
		_containerProxmoxAdapter = containerProxmoxAdapter;
		_fqdnService = fqdnService;
	}

	public bool IsEnabled => _config.CurrentValue.Export.Pihole is not null;

	public string Name => "Pi-hole";

	private Pihole Config => _config.CurrentValue.Export.Pihole ?? throw new InvalidOperationException("Pihole export configuration is not set.");

	public async Task ExportAsync(IReadOnlyCollection<IFqdnWithTimestamp> records, CancellationToken cancellationToken = default)
	{
		var content = _fqdnService.GetHostList(records.ToArray());
		await UpdateCustomFqdn(content);
	}

	private async Task UpdateCustomFqdn(string content)
	{
		_logger.LogInformation("Updating custom FQDN file for Pihole...");

		if (_config.CurrentValue.Export.Console)
		{
			_logger.LogInformation("New content for Pihole custom FQDN file:\n{Content}", content);
		}

		IProxmoxAdapter adapter = Config.Type switch
		{
			ProxmoxElementType.Container => _containerProxmoxAdapter,
			ProxmoxElementType.Vm => _vmProxmoxAdapter,
			_ => throw new ArgumentOutOfRangeException(nameof(Config.Type), "Unsupported Proxmox element type for Pihole export.")
		};

		_logger.LogInformation("Adapter type: {AdapterType}", adapter.GetType().Name);

		await adapter.WriteFile(Config.Id, Config.ListFilePath, content);

		await adapter.SendCommand(Config.Id, $"{Config.ExecutablePath} reloaddns");
	}
}
