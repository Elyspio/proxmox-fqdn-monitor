using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Models;

namespace ProxmoxIpMonitor.Adapters.Proxmox.Pve;

/// <summary>Supplies the HTTP client used to talk to a given node.</summary>
public interface IPveHttpClientProvider
{
	HttpClient Get(PveNode node);
}

/// <summary>
///     Hands out one <see cref="HttpClient" /> per node.
///     A dedicated client is needed because the certificate policy is a per-node setting and
///     is fixed at handler construction time. The cache key carries every field that affects
///     the handler, so editing a node's URL or TLS flag transparently yields a fresh client.
/// </summary>
public sealed class PveHttpClientProvider(ILogger<PveHttpClientProvider> logger)
	: TracingAdapter(logger), IPveHttpClientProvider, IDisposable
{
	private readonly ConcurrentDictionary<string, HttpClient> _clients = new();

	public void Dispose()
	{
		using var trace = LogAdapter();

		foreach (var client in _clients.Values) client.Dispose();
		_clients.Clear();
	}

	public HttpClient Get(PveNode node)
	{
		using var trace = LogAdapter($"{Log.F(node.DisplayName)}");

		var key = $"{node.Id}|{node.BaseUrl}|{node.AllowInvalidCertificate}";
		return _clients.GetOrAdd(key, _ => Create(node));
	}

	private static HttpClient Create(PveNode node)
	{
		var handler = new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(10)
		};

		if (node.AllowInvalidCertificate)
			// Proxmox ships a self-signed certificate on 8006. The operator opted into trusting
			// whatever answers on this address; the token travels over an unverified channel.
			handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

		return new HttpClient(handler)
		{
			BaseAddress = new Uri(node.BaseUrl.TrimEnd('/') + "/"),
			Timeout = TimeSpan.FromSeconds(30),
			DefaultRequestHeaders = { Accept = { new MediaTypeWithQualityHeaderValue("application/json") } }
		};
	}
}
