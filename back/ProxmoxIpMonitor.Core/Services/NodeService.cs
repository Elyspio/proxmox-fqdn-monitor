using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.Extensions.Logging;
using ProxmoxIpMonitor.Abstractions.Exceptions;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Transports;

namespace ProxmoxIpMonitor.Core.Services;

public sealed class NodeService(
	INodeRepository nodes,
	IHostRepository hosts,
	IPveClient pveClient,
	ISecretProtector protector,
	ILogger<NodeService> logger) : TracingService(logger), INodeService
{
	public async Task<IReadOnlyList<NodeDto>> GetAllAsync(CancellationToken ct = default)
	{
		using var trace = LogService();

		var stored = await nodes.GetAllAsync(ct);
		return stored.Select(NodeDto.From).ToList();
	}

	public async Task<NodeDto> CreateAsync(NodeWriteDto node, CancellationToken ct = default)
	{
		// The write DTO carries the raw token secret: only safe fields go through Log.F.
		using var trace = LogService($"{Log.F(node.DisplayName)}");

		if (string.IsNullOrWhiteSpace(node.TokenSecret))
			throw HttpException.BadRequest("A token secret is required when creating a node.");

		var created = await nodes.CreateAsync(new PveNode
		{
			DisplayName = node.DisplayName.Trim(),
			BaseUrl = node.BaseUrl.Trim(),
			NodeName = node.NodeName.Trim(),
			TokenId = node.TokenId.Trim(),
			TokenSecretProtected = protector.Protect(node.TokenSecret.Trim()),
			AllowInvalidCertificate = node.AllowInvalidCertificate,
			Enabled = node.Enabled
		}, ct);

		return NodeDto.From(created);
	}

	public async Task<NodeDto> UpdateAsync(string id, NodeWriteDto node, CancellationToken ct = default)
	{
		using var trace = LogService($"{Log.F(id)} {Log.F(node.DisplayName)}");

		var existing = await nodes.GetAsync(id, ct) ?? throw HttpException.NotFound($"Unknown node '{id}'.");

		// An empty secret means "unchanged": the UI never receives the stored value, so it
		// cannot echo it back, and blanking the field must not wipe the credential.
		var secret = string.IsNullOrWhiteSpace(node.TokenSecret)
			? existing.TokenSecretProtected
			: protector.Protect(node.TokenSecret.Trim());

		var updated = await nodes.UpdateAsync(existing with
		{
			DisplayName = node.DisplayName.Trim(),
			BaseUrl = node.BaseUrl.Trim(),
			NodeName = node.NodeName.Trim(),
			TokenId = node.TokenId.Trim(),
			TokenSecretProtected = secret,
			AllowInvalidCertificate = node.AllowInvalidCertificate,
			Enabled = node.Enabled
		}, ct);

		return NodeDto.From(updated);
	}

	public async Task DeleteAsync(string id, CancellationToken ct = default)
	{
		using var trace = LogService($"{Log.F(id)}");

		_ = await nodes.GetAsync(id, ct) ?? throw HttpException.NotFound($"Unknown node '{id}'.");

		// Drop the node's hosts with it, otherwise they linger in the snapshot with no
		// collector able to refresh or expire them.
		await hosts.DeleteByNodeAsync(id, ct);
		await nodes.DeleteAsync(id, ct);
	}

	public async Task<NodeTestResultDto> TestAsync(NodeWriteDto node, string? id, CancellationToken ct = default)
	{
		using var trace = LogService($"{Log.F(id)} {Log.F(node.DisplayName)}");

		string protectedSecret;

		if (!string.IsNullOrWhiteSpace(node.TokenSecret))
		{
			protectedSecret = protector.Protect(node.TokenSecret.Trim());
		}
		else
		{
			if (string.IsNullOrWhiteSpace(id))
				throw HttpException.BadRequest("A token secret is required to test a node that has not been saved yet.");

			var existing = await nodes.GetAsync(id, ct) ?? throw HttpException.NotFound($"Unknown node '{id}'.");
			protectedSecret = existing.TokenSecretProtected;
		}

		var candidate = new PveNode
		{
			// A distinct id keeps the probe from reusing a cached HttpClient built with the
			// saved node's certificate policy.
			Id = $"test-{Guid.NewGuid():N}",
			DisplayName = node.DisplayName.Trim(),
			BaseUrl = node.BaseUrl.Trim(),
			NodeName = node.NodeName.Trim(),
			TokenId = node.TokenId.Trim(),
			TokenSecretProtected = protectedSecret,
			AllowInvalidCertificate = node.AllowInvalidCertificate,
			Enabled = true
		};

		var result = await pveClient.TestAsync(candidate, ct);

		return result.Success
			? new NodeTestResultDto(true, result.Data)
			: new NodeTestResultDto(false, result.Error.Message);
	}
}
