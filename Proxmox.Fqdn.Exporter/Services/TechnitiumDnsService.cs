using System.Net.Http.Headers;
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

		_logger.LogInformation("Exporting {Count} A records to Technitium zone {Zone} on primary {Primary}", desiredRecords.Length, config.Zone, config.PrimaryNode ?? baseUri.Host);

		foreach (var record in desiredRecords)
		{
			await AddOrUpdateARecord(baseUri, config, record, cancellationToken);
		}
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
}
