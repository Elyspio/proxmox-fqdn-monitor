using ProxmoxIpMonitor.Abstractions.Models;
using ProxmoxIpMonitor.Abstractions.Technical;

namespace ProxmoxIpMonitor.Abstractions.Interfaces;

/// <summary>A host discovered on a node, before it is merged into the stored snapshot.</summary>
public sealed record DiscoveredHost
{
	public required HostType Type { get; init; }

	public required int VmId { get; init; }

	public required string Hostname { get; init; }

	/// <summary>Null when the guest reported no address inside the configured subnets.</summary>
	public string? Ip { get; init; }

	/// <summary>Set when <see cref="Ip" /> is null, explaining why.</summary>
	public string? Issue { get; init; }
}

/// <summary>Everything one poll of one node produced.</summary>
public sealed record NodeSnapshot
{
	public required IReadOnlyList<DiscoveredHost> Hosts { get; init; }
}

/// <summary>
///     Reads addresses from the Proxmox REST API. The only abstraction allowed to know
///     about /api2/json, PVEAPIToken headers, or the guest agent.
/// </summary>
public interface IPveClient
{
	/// <summary>
	///     Polls one node for the hypervisor's own address plus every running VM and container.
	///     Guest-level failures surface as <see cref="DiscoveredHost.Issue" /> rather than exceptions;
	///     only node-level failures (unreachable, bad token, TLS refused) produce a failed result.
	/// </summary>
	Task<Result<NodeSnapshot>> CollectAsync(PveNode node, IReadOnlyList<string> subnets, CancellationToken ct = default);

	/// <summary>Verifies credentials and TLS reachability before a node is saved.</summary>
	Task<Result<string>> TestAsync(PveNode node, CancellationToken ct = default);
}
