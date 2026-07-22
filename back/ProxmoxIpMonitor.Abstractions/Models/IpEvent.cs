namespace ProxmoxIpMonitor.Abstractions.Models;

public enum IpEventKind
{
	/// <summary>A host acquired an address for the first time, or came back after retention dropped it.</summary>
	Appeared,

	/// <summary>A known host now answers on a different address.</summary>
	Changed,

	/// <summary>A host stopped being reported, or lost its address.</summary>
	Disappeared,

	/// <summary>Proxmox reports a different name for the same vmid.</summary>
	Renamed
}

/// <summary>
///     Append-only record of a change. Never updated in place — the history screen reads
///     this collection directly, and a TTL index is what bounds its growth.
/// </summary>
public sealed record IpEvent
{
	public string? Id { get; init; }

	public required string HostKey { get; init; }

	public required string Hostname { get; init; }

	public required HostType Type { get; init; }

	public required string NodeName { get; init; }

	public required IpEventKind Kind { get; init; }

	public string? PreviousIp { get; init; }

	public string? CurrentIp { get; init; }

	public required DateTime At { get; init; }
}
