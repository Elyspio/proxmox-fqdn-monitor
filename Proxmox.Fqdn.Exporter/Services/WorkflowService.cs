using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Proxmox.Fqdn.Exporter.Abstractions.Interfaces.Services;
using Proxmox.Fqdn.Exporter.Data;
using Proxmox.Fqdn.Exporter.Options;
using Proxmox.Fqdn.Exporter.Repositories;

namespace Proxmox.Fqdn.Exporter.Services;

public class WorkflowService
{
	private readonly IEnumerable<IDnsProvider> _dnsProviders;
	private readonly FqdnService _fqdnService;
	private readonly ILogger<WorkflowService> _logger;
	private readonly FqdnRepository _fqdnRepository;
	private readonly IOptions<AppConfig> _appConfig;

	public WorkflowService(FqdnService fqdnService, IEnumerable<IDnsProvider> dnsProviders, ILogger<WorkflowService> logger, FqdnRepository fqdnRepository, IOptions<AppConfig> appConfig)
	{
		_fqdnService = fqdnService;
		_dnsProviders = dnsProviders;
		_logger = logger;
		_fqdnRepository = fqdnRepository;
		_appConfig = appConfig;
	}

	public async Task Run()
	{
		var now = Stopwatch.GetTimestamp();

		var fqdn = await GetCurrentFqdn();

		var thresholdDate = DateTime.UtcNow.Add(-TimeSpan.FromMinutes(_appConfig.Value.FqdnRetentionMinutes));

		await _fqdnRepository.DeleteOlderThan(thresholdDate);

		var oldFqdn = (await _fqdnRepository.GetAll()).Cast<IFqdnWithTimestamp>().ToArray();

		var newFqdn = fqdn.Concat(oldFqdn).GroupBy(f => f.Hostname).Select(g => g.First()).ToArray();

		await _fqdnRepository.DeleteAllByIps(newFqdn.Select(f => f.Ip).ToArray());

		await _fqdnRepository.AddRange(newFqdn!);

		if (_appConfig.Value.Export.Console)
		{
			var hostList = _fqdnService.GetHostList(newFqdn!);
			_logger.LogInformation("New FQDN host list:\n{Content}", hostList);
		}

		var enabledProviders = _dnsProviders.Where(provider => provider.IsEnabled).ToArray();
		foreach (var provider in enabledProviders)
		{
			_logger.LogInformation("Exporting FQDN records with {Provider}", provider.Name);
			await provider.ExportAsync(newFqdn);
		}

		_logger.LogInformation("FQDN Exporter completed in {TotalSeconds} seconds.", Stopwatch.GetElapsedTime(now).TotalSeconds);
	}

	private async Task<FqdnWithTimestamp[]> GetCurrentFqdn()
	{
		// Placeholder for future implementation

		var hostFqdn = _fqdnService.GetHostFqdn();
		var vmsFqdn = _fqdnService.GetVmsFqdn();
		var containersFqdn = _fqdnService.GetContainersFqdn();

		await Task.WhenAll(hostFqdn, vmsFqdn, containersFqdn);

		Data.Fqdn[] arr = [hostFqdn.Result, ..vmsFqdn.Result, ..containersFqdn.Result];

		foreach (var fqdn in vmsFqdn.Result)
		{
			_logger.LogInformation("VM FQDN: {Hostname} - {Ip}", fqdn.Hostname, fqdn.Ip);
		}

		foreach (var fqdn in containersFqdn.Result)
		{
			_logger.LogInformation("Container FQDN: {Hostname} - {Ip}", fqdn.Hostname, fqdn.Ip);
		}

		return arr.Select(fqdn => new FqdnWithTimestamp(fqdn.Ip, fqdn.Hostname, DateTime.Now)).ToArray();
	}
}
