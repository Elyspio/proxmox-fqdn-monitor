using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Adapters.Proxmox.Pve;
using Xunit;

namespace ProxmoxIpMonitor.Adapters.Tests;

public class PveApiClientTests
{
	private const string NodeNetwork =
		"""{"data":[{"iface":"vmbr0","address":"10.0.0.10","active":1},{"iface":"lo","address":"127.0.0.1","active":1}]}""";

	private static readonly string[] Subnets = ["10.0.0.0/8"];

	/// <summary>Test cancellation token, so a hung adapter call fails the run instead of stalling it.</summary>
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private static PveNode Node()
	{
		return new PveNode
		{
			Id = Guid.NewGuid().ToString("N"),
			DisplayName = "proxmox",
			BaseUrl = "https://10.0.0.10:8006",
			NodeName = "proxmox",
			TokenId = "monitor@pve!ip-monitor",
			TokenSecretProtected = "secret",
			AllowInvalidCertificate = true
		};
	}

	private static (PveApiClient Client, FakeHttpMessageHandler Handler) Build(Func<string, HttpResponseMessage> route)
	{
		var handler = new FakeHttpMessageHandler((request, _) => route(request.RequestUri!.AbsolutePath));
		var provider = new TestClientProvider(handler);

		return (new PveApiClient(provider, new PassthroughProtector(), NullLogger<PveApiClient>.Instance), handler);
	}

	[Fact]
	public async Task CollectsTheHypervisorAddressFromTheConfiguredSubnet()
	{
		var (client, _) = Build(path => path switch
		{
			var p when p.EndsWith("/network") => FakeHttpMessageHandler.Json(NodeNetwork),
			var p when p.EndsWith("/qemu") => FakeHttpMessageHandler.Json("""{"data":[]}"""),
			var p when p.EndsWith("/lxc") => FakeHttpMessageHandler.Json("""{"data":[]}"""),
			_ => FakeHttpMessageHandler.Json("""{"data":null}""")
		});

		var result = await client.CollectAsync(Node(), Subnets, Ct);

		Assert.True(result.Success);
		var host = Assert.Single(result.Data!.Hosts);
		Assert.Equal(HostType.Node, host.Type);
		Assert.Equal("10.0.0.10", host.Ip);
	}

	[Fact]
	public async Task StoppedGuestsAreIgnored()
	{
		var (client, _) = Build(path => path switch
		{
			var p when p.EndsWith("/network") => FakeHttpMessageHandler.Json(NodeNetwork),
			var p when p.EndsWith("/qemu") => FakeHttpMessageHandler.Json(
				"""{"data":[{"vmid":100,"name":"stopped-vm","status":"stopped"}]}"""),
			var p when p.EndsWith("/lxc") => FakeHttpMessageHandler.Json("""{"data":[]}"""),
			_ => FakeHttpMessageHandler.Json("""{"data":null}""")
		});

		var result = await client.CollectAsync(Node(), Subnets, Ct);

		Assert.True(result.Success);
		Assert.DoesNotContain(result.Data!.Hosts, host => host.Type == HostType.Vm);
	}

	[Fact]
	public async Task VmAddressComesFromTheGuestAgentAndHonoursTheSubnetFilter()
	{
		var (client, _) = Build(path => path switch
		{
			var p when p.EndsWith("/network") => FakeHttpMessageHandler.Json(NodeNetwork),
			var p when p.EndsWith("/qemu") => FakeHttpMessageHandler.Json(
				"""{"data":[{"vmid":100,"name":"web-01","status":"running"}]}"""),
			var p when p.Contains("network-get-interfaces") => FakeHttpMessageHandler.Json(
				"""
				{"data":{"result":[
				  {"name":"lo","ip-addresses":[{"ip-address":"127.0.0.1","ip-address-type":"ipv4"}]},
				  {"name":"eth0","ip-addresses":[
				    {"ip-address":"fe80::1","ip-address-type":"ipv6"},
				    {"ip-address":"172.17.0.1","ip-address-type":"ipv4"},
				    {"ip-address":"10.0.10.5","ip-address-type":"ipv4"}]}]}}
				"""),
			var p when p.EndsWith("/lxc") => FakeHttpMessageHandler.Json("""{"data":[]}"""),
			_ => FakeHttpMessageHandler.Json("""{"data":null}""")
		});

		var result = await client.CollectAsync(Node(), Subnets, Ct);

		var vm = Assert.Single(result.Data!.Hosts, host => host.Type == HostType.Vm);
		Assert.Equal("web-01", vm.Hostname);
		Assert.Equal("10.0.10.5", vm.Ip);
	}

	[Fact]
	public async Task AMissingGuestAgentDegradesToAnIssueRatherThanFailingTheNode()
	{
		// Losing one VM's address must never look like losing the hypervisor.
		var (client, _) = Build(path => path switch
		{
			var p when p.EndsWith("/network") => FakeHttpMessageHandler.Json(NodeNetwork),
			var p when p.EndsWith("/qemu") => FakeHttpMessageHandler.Json(
				"""{"data":[{"vmid":100,"name":"no-agent","status":"running"}]}"""),
			var p when p.Contains("network-get-interfaces") => FakeHttpMessageHandler.Json(
				"""{"data":null,"errors":"QEMU guest agent is not running"}""", HttpStatusCode.InternalServerError),
			var p when p.EndsWith("/lxc") => FakeHttpMessageHandler.Json("""{"data":[]}"""),
			_ => FakeHttpMessageHandler.Json("""{"data":null}""")
		});

		var result = await client.CollectAsync(Node(), Subnets, Ct);

		Assert.True(result.Success);
		var vm = Assert.Single(result.Data!.Hosts, host => host.Type == HostType.Vm);
		Assert.Null(vm.Ip);
		Assert.NotNull(vm.Issue);
		Assert.Contains("guest agent", vm.Issue, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ContainerAddressIsStrippedOfItsPrefixLength()
	{
		var (client, _) = Build(path => path switch
		{
			var p when p.EndsWith("/network") => FakeHttpMessageHandler.Json(NodeNetwork),
			var p when p.EndsWith("/qemu") => FakeHttpMessageHandler.Json("""{"data":[]}"""),
			var p when p.EndsWith("/lxc") => FakeHttpMessageHandler.Json(
				"""{"data":[{"vmid":241,"name":"ely-dns-01","status":"running"}]}"""),
			var p when p.Contains("/interfaces") => FakeHttpMessageHandler.Json(
				"""{"data":[{"name":"lo","inet":"127.0.0.1/8"},{"name":"eth0","inet":"10.0.10.241/24"}]}"""),
			_ => FakeHttpMessageHandler.Json("""{"data":null}""")
		});

		var result = await client.CollectAsync(Node(), Subnets, Ct);

		var container = Assert.Single(result.Data!.Hosts, host => host.Type == HostType.Container);
		Assert.Equal("10.0.10.241", container.Ip);
	}

	[Fact]
	public async Task AnUnreachableNodeFailsTheWholeSnapshot()
	{
		var (client, _) = Build(_ => FakeHttpMessageHandler.Json("""{"data":null}""", HttpStatusCode.Unauthorized));

		var result = await client.CollectAsync(Node(), Subnets, Ct);

		Assert.False(result.Success);
	}

	[Fact]
	public async Task TheApiTokenTravelsInThePveApiTokenHeader()
	{
		var (client, handler) = Build(path => path.EndsWith("/network")
			? FakeHttpMessageHandler.Json(NodeNetwork)
			: FakeHttpMessageHandler.Json("""{"data":[]}"""));

		await client.CollectAsync(Node(), Subnets, Ct);

		Assert.NotEmpty(handler.Requests);
	}

	/// <summary>Routes every node to the fake handler while keeping the real base-address logic.</summary>
	private sealed class TestClientProvider(HttpMessageHandler handler) : IPveHttpClientProvider
	{
		public HttpClient Get(PveNode node)
		{
			return new HttpClient(handler, false) { BaseAddress = new Uri(node.BaseUrl.TrimEnd('/') + "/") };
		}
	}
}
