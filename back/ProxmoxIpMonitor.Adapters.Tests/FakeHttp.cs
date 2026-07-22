using System.Net;
using System.Text;
using ProxmoxIpMonitor.Abstractions.Interfaces;

namespace ProxmoxIpMonitor.Adapters.Tests;

/// <summary>One request captured on its way to the wire.</summary>
public sealed record CapturedRequest(HttpMethod Method, string Path, string Query, string Body);

/// <summary>
///     Records every request and answers from a canned routing table, so the adapters can be
///     tested against exact payloads without a Proxmox node or a DNS server.
/// </summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
{
	public List<CapturedRequest> Requests { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

		Requests.Add(new CapturedRequest(
			request.Method,
			request.RequestUri?.AbsolutePath ?? "",
			request.RequestUri?.Query ?? "",
			body));

		return responder(request, body);
	}

	public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
	{
		return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
	}
}

/// <summary>Hands the adapters the fake handler through the factory they expect.</summary>
public sealed class FakeHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress = null) : IHttpClientFactory
{
	public HttpClient CreateClient(string name)
	{
		return new HttpClient(handler, false) { BaseAddress = baseAddress };
	}
}

/// <summary>Identity protector: the tests are about payloads, not about cryptography.</summary>
public sealed class PassthroughProtector : ISecretProtector
{
	public string Protect(string plaintext)
	{
		return plaintext;
	}

	public string Unprotect(string ciphertext)
	{
		return ciphertext;
	}
}
