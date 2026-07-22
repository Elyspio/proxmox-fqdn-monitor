using System.Net;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Exceptions;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Transports;

namespace ProxmoxIpMonitor.Core.Services;

public sealed class SettingsService(
	ISettingsRepository settings,
	ISecretProtector protector,
	ILogger<SettingsService> logger) : TracingService(logger), ISettingsService
{
	public async Task<SettingsDto> GetAsync(CancellationToken ct = default)
	{
		using var trace = LogService();

		return SettingsDto.From(await settings.GetAsync(ct));
	}

	public async Task<SettingsDto> UpdateAsync(SettingsWriteDto body, CancellationToken ct = default)
	{
		// The write DTO carries the raw Technitium token: nothing from it goes through Log.F.
		using var trace = LogService();

		var current = await settings.GetAsync(ct);

		foreach (var cidr in body.SubnetsFilter)
			if (!IsCidr(cidr))
				throw HttpException.BadRequest($"'{cidr}' is not valid CIDR notation (expected something like 10.0.0.0/8).");

		// Same rule as node tokens: an empty field keeps the stored secret.
		var apiToken = string.IsNullOrWhiteSpace(body.Technitium.ApiToken)
			? current.Technitium.ApiTokenProtected
			: protector.Protect(body.Technitium.ApiToken.Trim());

		if (body.Technitium.Enabled && string.IsNullOrEmpty(apiToken))
			throw HttpException.BadRequest("Technitium export cannot be enabled without an API token.");

		var updated = await settings.SaveAsync(current with
		{
			PollInterval = body.PollInterval,
			SubnetsFilter = body.SubnetsFilter,
			RetentionMinutes = body.RetentionMinutes,
			ExcludedHostnames = body.ExcludedHostnames,
			ReconciliationEnabled = body.ReconciliationEnabled,
			DeleteOrphanRecords = body.DeleteOrphanRecords,
			JournalRetentionDays = body.JournalRetentionDays,
			Technitium = new TechnitiumSettings
			{
				Enabled = body.Technitium.Enabled,
				BaseUrl = body.Technitium.BaseUrl.Trim(),
				ApiTokenProtected = apiToken,
				Zone = body.Technitium.Zone.Trim(),
				PrimaryNode = string.IsNullOrWhiteSpace(body.Technitium.PrimaryNode) ? null : body.Technitium.PrimaryNode.Trim(),
				RecordTtlSeconds = body.Technitium.RecordTtlSeconds,
				CreatePtr = body.Technitium.CreatePtr
			}
		}, ct);

		return SettingsDto.From(updated);
	}

	private static bool IsCidr(string value)
	{
		var parts = value.Split('/');
		return parts.Length == 2
		       && IPAddress.TryParse(parts[0], out _)
		       && int.TryParse(parts[1], out var prefix)
		       && prefix is >= 0 and <= 32;
	}
}
