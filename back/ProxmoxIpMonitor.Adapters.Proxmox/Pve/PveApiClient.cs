using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Technical;

namespace ProxmoxIpMonitor.Adapters.Proxmox.Pve;

/// <summary>
///     Reads addresses from the Proxmox REST API, replacing the qm/pct shell-outs of the
///     original on-host exporter.
///     Failure policy is deliberate: a guest that cannot answer (no agent, no address in the
///     configured subnets) is recorded as an issue and the poll continues, while a node that
///     cannot be reached or authenticated fails the whole snapshot. Losing one VM's address
///     must never look like losing the node.
/// </summary>
public sealed class PveApiClient(
	IPveHttpClientProvider clients,
	ISecretProtector protector,
	ILogger<PveApiClient> logger) : TracingAdapter(logger), IPveClient
{
	/// <summary>Proxmox reports running guests with this exact status string.</summary>
	private const string RunningStatus = "running";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	/// <inheritdoc />
	public async Task<Result<NodeSnapshot>> CollectAsync(PveNode node, IReadOnlyList<string> subnets, CancellationToken ct = default)
	{
		using var trace = LogAdapter($"{Log.F(node.DisplayName)} {Log.F(subnets)}");

		HttpClient client;
		AuthenticationHeaderValue auth;
		try
		{
			client = clients.Get(node);
			auth = BuildAuthHeader(node);
		}
		catch (Exception e)
		{
			return new InvalidOperationException($"Node '{node.DisplayName}' is misconfigured: {e.Message}", e);
		}

		var hosts = new List<DiscoveredHost>();

		// The hypervisor's own address. A node that cannot list its interfaces is unusable,
		// so this doubles as the reachability and authentication probe.
		var nodeAddress = await GetNodeAddressAsync(client, auth, node, subnets, ct);
		if (!nodeAddress.Success) return nodeAddress.Error;

		hosts.Add(new DiscoveredHost
		{
			Type = HostType.Node,
			VmId = 0,
			Hostname = node.NodeName,
			Ip = nodeAddress.Data.Length == 0 ? null : nodeAddress.Data,
			Issue = nodeAddress.Data.Length == 0 ? "No hypervisor address inside the configured subnets" : null
		});

		var vms = await ListGuestsAsync(client, auth, node, "qemu", ct);
		if (!vms.Success) return vms.Error;

		var containers = await ListGuestsAsync(client, auth, node, "lxc", ct);
		if (!containers.Success) return containers.Error;

		// Bounded fan-out: a node with many guests should not open one connection per guest.
		using var gate = new SemaphoreSlim(8);

		var vmTasks = vms.Data.Select(guest => ResolveAsync(guest, HostType.Vm,
			() => GetVmIpAsync(client, auth, node, guest.VmId, subnets, ct), gate, ct));

		var containerTasks = containers.Data.Select(guest => ResolveAsync(guest, HostType.Container,
			() => GetContainerIpAsync(client, auth, node, guest.VmId, subnets, ct), gate, ct));

		hosts.AddRange(await Task.WhenAll(vmTasks.Concat(containerTasks)));

		return new NodeSnapshot { Hosts = hosts };
	}

	/// <inheritdoc />
	public async Task<Result<string>> TestAsync(PveNode node, CancellationToken ct = default)
	{
		using var trace = LogAdapter($"{Log.F(node.DisplayName)}");

		try
		{
			var client = clients.Get(node);
			var auth = BuildAuthHeader(node);

			// /version is the cheapest authenticated endpoint; it proves TLS, routing and token.
			using var request = new HttpRequestMessage(HttpMethod.Get, "api2/json/version");
			request.Headers.Authorization = auth;
			using var response = await client.SendAsync(request, ct);

			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
				return new InvalidOperationException("Proxmox rejected the API token (check the token id, its secret, and that privilege separation grants the required role).");

			if (!response.IsSuccessStatusCode)
				return new InvalidOperationException($"Proxmox answered HTTP {(int)response.StatusCode} on /version.");

			// Listing the node's interfaces proves the node name is right and Sys.Audit is granted.
			// No type filter: PVE 9 dropped 'any' from the enum, and the unfiltered list is what
			// GetNodeAddressAsync already scans anyway.
			using var nodeRequest = new HttpRequestMessage(HttpMethod.Get, $"api2/json/nodes/{Uri.EscapeDataString(node.NodeName)}/network");
			nodeRequest.Headers.Authorization = auth;
			using var nodeResponse = await client.SendAsync(nodeRequest, ct);

			if (!nodeResponse.IsSuccessStatusCode)
				return new InvalidOperationException(
					$"Reached Proxmox, but node '{node.NodeName}' answered HTTP {(int)nodeResponse.StatusCode}. Check the node name and the token's Sys.Audit permission.");

			return "Connection succeeded.";
		}
		catch (Exception e)
		{
			return Describe(e, node);
		}
	}

	private async Task<DiscoveredHost> ResolveAsync(PveGuest guest, HostType type, Func<Task<Result<string>>> resolve, SemaphoreSlim gate, CancellationToken ct)
	{
		await gate.WaitAsync(ct);
		try
		{
			var name = string.IsNullOrWhiteSpace(guest.Name) ? $"{type.ToString().ToLowerInvariant()}-{guest.VmId}" : guest.Name;
			var ip = await resolve();

			if (ip.Success && ip.Data.Length > 0)
				return new DiscoveredHost { Type = type, VmId = guest.VmId, Hostname = name, Ip = ip.Data };

			var issue = ip.Success
				? "No IPv4 address inside the configured subnets"
				: ip.Error.Message;

			logger.LogDebug("No usable address for {Type} {VmId} ({Name}): {Issue}", type, guest.VmId, name, issue);

			return new DiscoveredHost { Type = type, VmId = guest.VmId, Hostname = name, Ip = null, Issue = issue };
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task<Result<string>> GetNodeAddressAsync(HttpClient client, AuthenticationHeaderValue auth, PveNode node, IReadOnlyList<string> subnets, CancellationToken ct)
	{
		try
		{
			var interfaces = await GetAsync<List<PveNetworkInterface>>(client, auth, $"api2/json/nodes/{Uri.EscapeDataString(node.NodeName)}/network", ct);

			var address = interfaces?
				.Where(i => i.Active is null or 1)
				.Select(i => StripMask(i.Address ?? i.Cidr))
				.FirstOrDefault(ip => ip is not null && SubnetFilter.IsInAny(subnets, ip));

			return address ?? "";
		}
		catch (Exception e)
		{
			return Describe(e, node);
		}
	}

	private async Task<Result<List<PveGuest>>> ListGuestsAsync(HttpClient client, AuthenticationHeaderValue auth, PveNode node, string kind, CancellationToken ct)
	{
		try
		{
			var guests = await GetAsync<List<PveGuest>>(client, auth, $"api2/json/nodes/{Uri.EscapeDataString(node.NodeName)}/{kind}", ct);

			// Stopped guests have no address to report and must not keep a DNS record alive.
			return guests?.Where(g => string.Equals(g.Status, RunningStatus, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
		}
		catch (Exception e)
		{
			return Describe(e, node);
		}
	}

	private async Task<Result<string>> GetVmIpAsync(HttpClient client, AuthenticationHeaderValue auth, PveNode node, int vmId, IReadOnlyList<string> subnets, CancellationToken ct)
	{
		try
		{
			// Requires VM.Monitor, not just VM.Audit: this proxies a guest-agent command.
			var payload = await GetAsync<PveAgentInterfaces>(client, auth,
				$"api2/json/nodes/{Uri.EscapeDataString(node.NodeName)}/qemu/{vmId}/agent/network-get-interfaces", ct);

			var address = payload?.Result?
				.SelectMany(i => i.IpAddresses ?? [])
				.Where(a => string.Equals(a.IpAddressType, "ipv4", StringComparison.OrdinalIgnoreCase))
				.Select(a => a.IpAddress)
				.FirstOrDefault(ip => ip is not null && SubnetFilter.IsInAny(subnets, ip));

			return address ?? "";
		}
		catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadRequest)
		{
			// Proxmox reports a missing or unresponsive guest agent as a 500/400 on this route.
			return new InvalidOperationException("QEMU guest agent is not responding");
		}
		catch (Exception e)
		{
			return e;
		}
	}

	private async Task<Result<string>> GetContainerIpAsync(HttpClient client, AuthenticationHeaderValue auth, PveNode node, int vmId, IReadOnlyList<string> subnets, CancellationToken ct)
	{
		try
		{
			var interfaces = await GetAsync<List<PveLxcInterface>>(client, auth,
				$"api2/json/nodes/{Uri.EscapeDataString(node.NodeName)}/lxc/{vmId}/interfaces", ct);

			var address = interfaces?
				.Select(i => StripMask(i.Inet))
				.FirstOrDefault(ip => ip is not null && SubnetFilter.IsInAny(subnets, ip));

			return address ?? "";
		}
		catch (Exception e)
		{
			return e;
		}
	}

	private static async Task<T?> GetAsync<T>(HttpClient client, AuthenticationHeaderValue auth, string path, CancellationToken ct)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		request.Headers.Authorization = auth;

		using var response = await client.SendAsync(request, ct);
		response.EnsureSuccessStatusCode();

		var payload = await response.Content.ReadFromJsonAsync<PveResponse<T>>(JsonOptions, ct);
		return payload is null ? default : payload.Data;
	}

	/// <summary>Turns "10.0.10.5/24" into "10.0.10.5"; passes a bare address through unchanged.</summary>
	private static string? StripMask(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		var slash = value.IndexOf('/');
		return slash < 0 ? value.Trim() : value[..slash].Trim();
	}

	private AuthenticationHeaderValue BuildAuthHeader(PveNode node)
	{
		var secret = protector.Unprotect(node.TokenSecretProtected);
		// Proxmox expects the whole credential in one header value, not a bearer token.
		return new AuthenticationHeaderValue("PVEAPIToken", $"{node.TokenId}={secret}");
	}

	/// <summary>Turns transport failures into messages an operator can act on.</summary>
	private static Exception Describe(Exception e, PveNode node)
	{
		return e switch
		{
			HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } =>
				new InvalidOperationException(
					$"TLS handshake with {node.BaseUrl} failed. Proxmox uses a self-signed certificate by default; enable \"accept invalid certificate\" for this node if that is expected.", e),
			HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
				new InvalidOperationException("Proxmox rejected the API token. The role must grant Sys.Audit, VM.Audit and VM.Monitor.", e),
			HttpRequestException =>
				new InvalidOperationException($"Could not reach {node.BaseUrl}: {e.Message}", e),
			TaskCanceledException =>
				new TimeoutException($"{node.BaseUrl} did not answer within the timeout.", e),
			_ => e
		};
	}
}
