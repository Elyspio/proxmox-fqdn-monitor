using System.Net.Http.Headers;
using System.Text.Json;
using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Adapters.Dns.Technitium;

/// <summary>
///     Thin transport over the Technitium web API. Kept separate from the reconciliation logic
///     so tests can drive the decision-making through a fake HttpMessageHandler and assert on
///     the exact form-encoded payloads that reach the wire.
/// </summary>
internal sealed class TechnitiumApi(HttpClient httpClient, ILogger logger) : TracingAdapter(logger)
{
	/// <summary>Lists the zone's enabled A records, keyed by fully qualified domain name.</summary>
	public async Task<IReadOnlyList<ExistingRecord>> ListARecordsAsync(TechnitiumSettings config, string apiToken, CancellationToken ct)
	{
		// The API token is a secret: it never goes through Log.F.
		using var trace = LogAdapter($"{Log.F(config.Zone)}");

		var baseUri = BuildBaseUri(config.BaseUrl);
		var zone = Uri.EscapeDataString(config.Zone);
		var uri = new Uri(baseUri, $"/api/zones/records/get?domain={zone}&zone={zone}&listZone=true");

		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

		using var response = await httpClient.SendAsync(request, ct);
		var responseText = await response.Content.ReadAsStringAsync(ct);

		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException($"Technitium returned HTTP {(int)response.StatusCode} when listing zone {config.Zone}: {responseText}");

		using var document = JsonDocument.Parse(responseText);
		var root = document.RootElement;
		EnsureOk(root, responseText, $"list zone {config.Zone}");

		var records = new List<ExistingRecord>();

		if (!root.TryGetProperty("response", out var responseElement)
		    || !responseElement.TryGetProperty("records", out var recordsElement)
		    || recordsElement.ValueKind != JsonValueKind.Array)
			return records;

		foreach (var entry in recordsElement.EnumerateArray())
		{
			if (!entry.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "A", StringComparison.OrdinalIgnoreCase))
				continue;

			if (entry.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
				continue;

			if (!entry.TryGetProperty("name", out var name) || string.IsNullOrEmpty(name.GetString()))
				continue;

			if (!entry.TryGetProperty("rData", out var rData)
			    || !rData.TryGetProperty("ipAddress", out var ipAddress)
			    || string.IsNullOrEmpty(ipAddress.GetString()))
				continue;

			var ttl = entry.TryGetProperty("ttl", out var ttlElement) && ttlElement.TryGetInt32(out var ttlValue) ? ttlValue : 0;
			var comments = entry.TryGetProperty("comments", out var commentsElement) ? commentsElement.GetString() : null;

			records.Add(new ExistingRecord(name.GetString()!, ipAddress.GetString()!, ttl, comments));
		}

		return records;
	}

	public async Task AddOrUpdateAsync(TechnitiumSettings config, string apiToken, DesiredRecord record, string ownershipMarker, CancellationToken ct)
	{
		using var trace = LogAdapter($"{Log.F(record.Domain)} {Log.F(record.Ip)}");

		var payload = new Dictionary<string, string>
		{
			["domain"] = record.Domain,
			["zone"] = config.Zone,
			["type"] = "A",
			["ttl"] = config.RecordTtlSeconds.ToString(),
			["ipAddress"] = record.Ip,
			["overwrite"] = "true",
			["ptr"] = config.CreatePtr ? "true" : "false",
			// Ownership marker. This is what later authorises deleting the record, and what
			// keeps hand-written records structurally out of reach of reconciliation.
			["comments"] = ownershipMarker
		};

		// Deliberately no expiryTtl: record aging forces a rewrite on every single run, which is
		// what bloated the primary zone's IXFR history. Stale records are removed explicitly instead.

		if (!string.IsNullOrWhiteSpace(config.PrimaryNode)) payload["node"] = config.PrimaryNode;

		await PostAsync(config, apiToken, "/api/zones/records/add", payload, $"upsert {record.Domain}", ct);
	}

	public async Task DeleteAsync(TechnitiumSettings config, string apiToken, ExistingRecord record, CancellationToken ct)
	{
		using var trace = LogAdapter($"{Log.F(record.Domain)} {Log.F(record.Ip)}");

		var payload = new Dictionary<string, string>
		{
			["domain"] = record.Domain,
			["zone"] = config.Zone,
			["type"] = "A",
			["ipAddress"] = record.Ip
		};

		if (!string.IsNullOrWhiteSpace(config.PrimaryNode)) payload["node"] = config.PrimaryNode;

		await PostAsync(config, apiToken, "/api/zones/records/delete", payload, $"delete {record.Domain}", ct);
	}

	private async Task PostAsync(TechnitiumSettings config, string apiToken, string path, Dictionary<string, string> payload, string what, CancellationToken ct)
	{
		var uri = new Uri(BuildBaseUri(config.BaseUrl), path);

		using var request = new HttpRequestMessage(HttpMethod.Post, uri);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
		request.Content = new FormUrlEncodedContent(payload);

		using var response = await httpClient.SendAsync(request, ct);
		var responseText = await response.Content.ReadAsStringAsync(ct);

		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException($"Technitium returned HTTP {(int)response.StatusCode} for {what}: {responseText}");

		using var document = JsonDocument.Parse(responseText);
		EnsureOk(document.RootElement, responseText, what);
	}

	/// <summary>Technitium answers HTTP 200 with a status field even for failures.</summary>
	private static void EnsureOk(JsonElement root, string responseText, string what)
	{
		if (root.TryGetProperty("status", out var status) && string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
			return;

		var error = root.TryGetProperty("errorMessage", out var errorMessage) ? errorMessage.GetString() : responseText;
		throw new InvalidOperationException($"Technitium failed to {what}: {error}");
	}

	internal static Uri BuildBaseUri(string baseUrl)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
			throw new InvalidOperationException($"Invalid Technitium BaseUrl: {baseUrl}");

		return uri;
	}
}
