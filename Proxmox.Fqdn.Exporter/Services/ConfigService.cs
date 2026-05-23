using Microsoft.Extensions.Options;
using Proxmox.Fqdn.Exporter.Options;

namespace Proxmox.Fqdn.Exporter.Services;

public class ConfigService
{
	private readonly IOptionsMonitor<AppConfig> _config;

	public ConfigService(IOptionsMonitor<AppConfig> config)
	{
		_config = config;
	}

	public void Verify()
	{
		Verify(_config.CurrentValue);
		_config.OnChange(Verify);
	}


	private void Verify(AppConfig config)
	{
		if (config.SubnetsFilter.Length == 0) throw new ArgumentException("SubnetsFilter is required in the configuration.");

		switch (config.Export)
		{
			case null:
				throw new ArgumentException("Export configuration is required.");
			case { Console: false, Pihole: null, Dns.Technitium: null }:
				throw new ArgumentException("At least one export method must be enabled: Console, Pihole, or Dns.Technitium.");
		}

		VerifyTechnitium(config.Export.Dns?.Technitium);
	}

	private static void VerifyTechnitium(Technitium? config)
	{
		if (config is null)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(config.BaseUrl))
		{
			throw new ArgumentException("Export:Dns:Technitium:BaseUrl is required.");
		}

		if (string.IsNullOrWhiteSpace(config.ApiToken))
		{
			throw new ArgumentException("Export:Dns:Technitium:ApiToken is required.");
		}

		if (string.IsNullOrWhiteSpace(config.Zone))
		{
			throw new ArgumentException("Export:Dns:Technitium:Zone is required.");
		}

		if (config.RecordTtlSeconds < 1)
		{
			throw new ArgumentException("Export:Dns:Technitium:RecordTtlSeconds must be greater than zero.");
		}
	}
}
