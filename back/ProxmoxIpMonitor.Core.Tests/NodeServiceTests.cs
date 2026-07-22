using Microsoft.Extensions.Logging.Abstractions;
using ProxmoxIpMonitor.Abstractions.Exceptions;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Technical;
using ProxmoxIpMonitor.Abstractions.Transports;
using ProxmoxIpMonitor.Core.Services;
using Xunit;

namespace ProxmoxIpMonitor.Core.Tests;

public class NodeServiceTests
{
	private static NodeWriteDto Write(string? secret = null)
	{
		return new NodeWriteDto
		{
			DisplayName = "pve",
			BaseUrl = "https://pve.example:8006",
			NodeName = "pve",
			TokenId = "monitor@pve!token",
			TokenSecret = secret,
			Enabled = true
		};
	}

	private static (NodeService Service, FakeNodeRepository Nodes, FakeHostRepository Hosts, FakePveClient Pve) Build(params PveNode[] stored)
	{
		var nodes = new FakeNodeRepository(stored);
		var hosts = new FakeHostRepository();
		var pve = new FakePveClient();
		var service = new NodeService(nodes, hosts, pve, new PassthroughProtector(), NullLogger<NodeService>.Instance);
		return (service, nodes, hosts, pve);
	}

	[Fact]
	public async Task Create_requires_a_secret()
	{
		var (service, _, _, _) = Build();

		var exception = await Assert.ThrowsAsync<HttpException>(() => service.CreateAsync(Write(), TestContext.Current.CancellationToken));
		Assert.Equal(System.Net.HttpStatusCode.BadRequest, exception.StatusCode);
	}

	[Fact]
	public async Task Create_protects_the_secret_and_never_returns_it()
	{
		var (service, nodes, _, _) = Build();

		var dto = await service.CreateAsync(Write("s3cret"), TestContext.Current.CancellationToken);

		Assert.True(dto.HasToken);
		Assert.Equal("protected:s3cret", nodes.Stored.Single().TokenSecretProtected);
	}

	[Fact]
	public async Task Update_with_empty_secret_keeps_the_stored_one()
	{
		var (service, nodes, _, _) = Build(new PveNode
		{
			Id = "n1",
			DisplayName = "pve",
			BaseUrl = "https://pve.example:8006",
			NodeName = "pve",
			TokenId = "monitor@pve!token",
			TokenSecretProtected = "protected:original",
			Enabled = true
		});

		var dto = await service.UpdateAsync("n1", Write(), TestContext.Current.CancellationToken);

		Assert.True(dto.HasToken);
		Assert.Equal("protected:original", nodes.Stored.Single().TokenSecretProtected);
	}

	[Fact]
	public async Task Delete_cascades_to_the_node_hosts()
	{
		var (service, nodes, hosts, _) = Build(new PveNode
		{
			Id = "n1",
			DisplayName = "pve",
			BaseUrl = "https://pve.example:8006",
			NodeName = "pve",
			TokenId = "monitor@pve!token",
			TokenSecretProtected = "protected:x",
			Enabled = true
		});

		await service.DeleteAsync("n1", TestContext.Current.CancellationToken);

		Assert.Empty(nodes.Stored);
		Assert.Equal(["n1"], hosts.DeletedNodeIds);
	}

	[Fact]
	public async Task Test_of_an_unsaved_node_requires_a_secret()
	{
		var (service, _, _, _) = Build();

		var exception = await Assert.ThrowsAsync<HttpException>(() => service.TestAsync(Write(), null, TestContext.Current.CancellationToken));
		Assert.Equal(System.Net.HttpStatusCode.BadRequest, exception.StatusCode);
	}

	[Fact]
	public async Task Test_probes_with_an_ephemeral_node_id()
	{
		var (service, _, _, pve) = Build();

		var result = await service.TestAsync(Write("s3cret"), null, TestContext.Current.CancellationToken);

		Assert.True(result.Success);
		Assert.StartsWith("test-", pve.LastTested!.Id);
	}

	private sealed class FakeNodeRepository(IEnumerable<PveNode> seed) : INodeRepository
	{
		public List<PveNode> Stored { get; } = [..seed];

		public Task<IReadOnlyList<PveNode>> GetAllAsync(CancellationToken ct = default)
		{
			return Task.FromResult<IReadOnlyList<PveNode>>(Stored);
		}

		public Task<PveNode?> GetAsync(string id, CancellationToken ct = default)
		{
			return Task.FromResult(Stored.FirstOrDefault(node => node.Id == id));
		}

		public Task<PveNode> CreateAsync(PveNode node, CancellationToken ct = default)
		{
			var created = node with { Id = $"id-{Stored.Count + 1}" };
			Stored.Add(created);
			return Task.FromResult(created);
		}

		public Task<PveNode> UpdateAsync(PveNode node, CancellationToken ct = default)
		{
			Stored.RemoveAll(stored => stored.Id == node.Id);
			Stored.Add(node);
			return Task.FromResult(node);
		}

		public Task DeleteAsync(string id, CancellationToken ct = default)
		{
			Stored.RemoveAll(stored => stored.Id == id);
			return Task.CompletedTask;
		}
	}

	private sealed class FakeHostRepository : IHostRepository
	{
		public List<string> DeletedNodeIds { get; } = [];

		public Task<IReadOnlyList<MonitoredHost>> GetAllAsync(CancellationToken ct = default)
		{
			return Task.FromResult<IReadOnlyList<MonitoredHost>>([]);
		}

		public Task<MonitoredHost?> GetAsync(string key, CancellationToken ct = default)
		{
			return Task.FromResult<MonitoredHost?>(null);
		}

		public Task UpsertManyAsync(IReadOnlyCollection<MonitoredHost> hosts, CancellationToken ct = default)
		{
			return Task.CompletedTask;
		}

		public Task DeleteManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
		{
			return Task.CompletedTask;
		}

		public Task DeleteByNodeAsync(string nodeId, CancellationToken ct = default)
		{
			DeletedNodeIds.Add(nodeId);
			return Task.CompletedTask;
		}
	}

	private sealed class FakePveClient : IPveClient
	{
		public PveNode? LastTested { get; private set; }

		public Task<Result<NodeSnapshot>> CollectAsync(PveNode node, IReadOnlyList<string> subnets, CancellationToken ct = default)
		{
			return Task.FromResult<Result<NodeSnapshot>>(new NodeSnapshot { Hosts = [] });
		}

		public Task<Result<string>> TestAsync(PveNode node, CancellationToken ct = default)
		{
			LastTested = node;
			return Task.FromResult<Result<string>>("ok");
		}
	}
}

internal sealed class PassthroughProtector : ISecretProtector
{
	public string Protect(string plaintext)
	{
		return plaintext.Length == 0 ? "" : $"protected:{plaintext}";
	}

	public string Unprotect(string ciphertext)
	{
		return ciphertext.StartsWith("protected:") ? ciphertext["protected:".Length..] : ciphertext;
	}
}
