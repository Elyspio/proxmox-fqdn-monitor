using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Proxmox.Fqdn.Exporter.Abstractions.Interfaces.Services;
using Proxmox.Fqdn.Exporter.Data;
using Proxmox.Fqdn.Exporter.Options;

namespace Proxmox.Fqdn.Exporter.Services;

/// <summary>
///     Exports Proxmox FQDN records to a Technitium DNS primary zone.
/// </summary>
public class TechnitiumDnsService : IDnsProvider
{
	private readonly HttpClient _httpClient;
	private readonly ILogger<TechnitiumDnsService> _logger;
	private readonly IOptionsMonitor<AppConfig> _config;

	/// <summary>
	///     Initializes a new instance of the <see cref="TechnitiumDnsService" /> class.
	/// </summary>
	/// <param name="httpClient">The HTTP client used to call Technitium.</param>
	/// <param name="logger">The logger.</param>
	/// <param name="config">The application configuration.</param>
	public TechnitiumDnsService(HttpClient httpClient, ILogger<TechnitiumDnsService> logger, IOptionsMonitor<AppConfig> config)
	{
		_httpClient = httpClient;
		_logger = logger;
		_config = config;
	}

	/// <inheritdoc />
	public bool IsEnabled => _config.CurrentValue.Export.Dns?.Technitium is not null;

	/// <inheritdoc />
	public string Name => "Technitium";

	private Technitium Config => _config.CurrentValue.Export.Dns?.Technitium ?? throw new InvalidOperationException("Technitium DNS export configuration is not set.");

	/// <inheritdoc />
	public async Task ExportAsync(IReadOnlyCollection<IFqdnWithTimestamp> records, CancellationToken cancellationToken = default)
	{
		var config = Config;
		var baseUri = BuildBaseUri(config.BaseUrl);
		var desiredRecords = records
			.Select(record => new DnsARecord(BuildDomainName(record.Hostname, config.Zone), record.Ip))
			.DistinctBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.OrderBy(record => record.Domain, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		// Record aging (expiryTtl) requires every record to be re-written on every run, otherwise Technitium
		// removes it once it expires. That refresh-on-every-run behaviour is exactly what bloats the primary
		// zone's IXFR change history and drives the DNS server's memory usage up, so it is incompatible with the
		// idempotent diffing below. Warn loudly rather than silently letting live records expire.
		if (config.RecordExpirySeconds is > 0)
		{
			_logger.LogWarning(
				"RecordExpirySeconds is set ({Expiry}s); idempotent skipping is disabled because expiring records must be refreshed every run. This causes IXFR history growth on the primary zone. Prefer leaving RecordExpirySeconds unset and reconciling stale records explicitly.",
				config.RecordExpirySeconds);
		}

		var existingRecords = await GetExistingARecords(baseUri, config, cancellationToken);

		_logger.LogInformation("Exporting {Count} A records to Technitium zone {Zone} on primary {Primary}", desiredRecords.Length, config.Zone, config.PrimaryNode ?? baseUri.Host);

		var written = 0;
		var skipped = 0;
		foreach (var record in desiredRecords)
		{
			if (IsUpToDate(existingRecords, config, record))
			{
				skipped++;
				_logger.LogDebug("Skipping unchanged A record {Domain} -> {Ip}", record.Domain, record.Ip);
				continue;
			}

			await AddOrUpdateARecord(baseUri, config, record, cancellationToken);
			written++;
		}

		_logger.LogInformation("Technitium export complete for zone {Zone}: {Written} written, {Skipped} unchanged", config.Zone, written, skipped);
	}

	/// <summary>
	///     Determines whether the zone already holds exactly the desired A record, so no write is needed.
	/// </summary>
	private bool IsUpToDate(IReadOnlyDictionary<string, IReadOnlyList<ExistingARecord>> existingRecords, Technitium config, DnsARecord record)
	{
		// When expiry is configured we must always re-write to keep the record alive.
		if (config.RecordExpirySeconds is > 0)
		{
			return false;
		}

		return existingRecords.TryGetValue(record.Domain, out var current)
			&& current.Count == 1
			&& string.Equals(current[0].Ip, record.Ip, StringComparison.OrdinalIgnoreCase)
			&& current[0].Ttl == config.RecordTtlSeconds;
	}

	/// <summary>
	///     Lists the current A records of the target zone, keyed by fully qualified domain name.
	/// </summary>
	private async Task<IReadOnlyDictionary<string, IReadOnlyList<ExistingARecord>>> GetExistingARecords(Uri baseUri, Technitium config, CancellationToken cancellationToken)
	{
		var zone = Uri.EscapeDataString(config.Zone);
		var uri = new Uri(baseUri, $"/api/zones/records/get?domain={zone}&zone={zone}&listZone=true");
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken);

		using var response = await _httpClient.SendAsync(request, cancellationToken);
		var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Technitium returned HTTP {(int)response.StatusCode} when listing zone {config.Zone}: {responseText}");
		}

		using var document = JsonDocument.Parse(responseText);
		var root = document.RootElement;

		if (!root.TryGetProperty("status", out var status) || !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
		{
			var error = root.TryGetProperty("errorMessage", out var errorMessage) ? errorMessage.GetString() : responseText;
			throw new InvalidOperationException($"Technitium failed to list zone {config.Zone}: {error}");
		}

		var result = new Dictionary<string, List<ExistingARecord>>(StringComparer.OrdinalIgnoreCase);

		if (root.TryGetProperty("response", out var responseElement)
			&& responseElement.TryGetProperty("records", out var recordsElement)
			&& recordsElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var entry in recordsElement.EnumerateArray())
			{
				if (!entry.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "A", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (entry.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
				{
					continue;
				}

				if (!entry.TryGetProperty("name", out var name) || string.IsNullOrEmpty(name.GetString()))
				{
					continue;
				}

				if (!entry.TryGetProperty("rData", out var rData) || !rData.TryGetProperty("ipAddress", out var ipAddress) || string.IsNullOrEmpty(ipAddress.GetString()))
				{
					continue;
				}

				var ttl = entry.TryGetProperty("ttl", out var ttlElement) && ttlElement.TryGetInt32(out var ttlValue) ? ttlValue : 0;

				var domain = name.GetString()!;
				if (!result.TryGetValue(domain, out var list))
				{
					list = [];
					result[domain] = list;
				}

				list.Add(new ExistingARecord(ipAddress.GetString()!, ttl));
			}
		}

		return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ExistingARecord>)pair.Value, StringComparer.OrdinalIgnoreCase);
	}

	private async Task AddOrUpdateARecord(Uri baseUri, Technitium config, DnsARecord record, CancellationToken cancellationToken)
	{
		var uri = new Uri(baseUri, "/api/zones/records/add");
		using var request = new HttpRequestMessage(HttpMethod.Post, uri);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken);
		request.Content = new FormUrlEncodedContent(BuildRecordPayload(config, record));

		using var response = await _httpClient.SendAsync(request, cancellationToken);
		var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Technitium returned HTTP {(int)response.StatusCode} for {record.Domain}: {responseText}");
		}

		EnsureTechnitiumSuccess(record.Domain, responseText);
		_logger.LogDebug("Upserted A record {Domain} -> {Ip}", record.Domain, record.Ip);
	}

	private static Dictionary<string, string> BuildRecordPayload(Technitium config, DnsARecord record)
	{
		var payload = new Dictionary<string, string>
		{
			["domain"] = record.Domain,
			["zone"] = config.Zone,
			["type"] = "A",
			["ttl"] = config.RecordTtlSeconds.ToString(),
			["ipAddress"] = record.Ip,
			["overwrite"] = "true",
			["ptr"] = config.CreatePtr ? "true" : "false"
		};

		if (config.RecordExpirySeconds is > 0)
		{
			payload["expiryTtl"] = config.RecordExpirySeconds.Value.ToString();
		}

		if (!string.IsNullOrWhiteSpace(config.PrimaryNode))
		{
			payload["node"] = config.PrimaryNode;
		}

		return payload;
	}

	private static void EnsureTechnitiumSuccess(string domain, string responseText)
	{
		using var document = System.Text.Json.JsonDocument.Parse(responseText);
		var root = document.RootElement;

		if (!root.TryGetProperty("status", out var status) || !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
		{
			var error = root.TryGetProperty("errorMessage", out var errorMessage) ? errorMessage.GetString() : responseText;
			throw new InvalidOperationException($"Technitium failed to upsert {domain}: {error}");
		}
	}

	private static Uri BuildBaseUri(string baseUrl)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
		{
			throw new InvalidOperationException($"Invalid Technitium BaseUrl: {baseUrl}");
		}

		return uri;
	}

	private static string BuildDomainName(string hostname, string zone)
	{
		var normalizedHostname = hostname.Trim().TrimEnd('.');
		var normalizedZone = zone.Trim().Trim('.');

		if (normalizedHostname.EndsWith($".{normalizedZone}", StringComparison.OrdinalIgnoreCase) || string.Equals(normalizedHostname, normalizedZone, StringComparison.OrdinalIgnoreCase))
		{
			return normalizedHostname;
		}

		return $"{normalizedHostname}.{normalizedZone}";
	}

	private sealed record DnsARecord(string Domain, string Ip);

	private sealed record ExistingARecord(string Ip, int Ttl);
}
