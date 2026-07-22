namespace ProxmoxIpMonitor.Abstractions.Models;

public enum CollectionOutcome
{
	Succeeded,

	/// <summary>The node answered, but some guests could not be resolved.</summary>
	Partial,

	/// <summary>The node could not be reached or refused the token.</summary>
	Failed
}

/// <summary>Why a specific guest produced no usable address.</summary>
public sealed record HostIssue
{
	public required int VmId { get; init; }

	public required string Hostname { get; init; }

	public required HostType Type { get; init; }

	public required string Reason { get; init; }
}

/// <summary>
///     Outcome of polling one node once. Written on every cycle, including failures —
///     this is what the health screen shows instead of the log file nobody read.
/// </summary>
public sealed record CollectionRun
{
	public string? Id { get; init; }

	public required string NodeId { get; init; }

	public required string NodeName { get; init; }

	public required DateTime StartedAt { get; init; }

	public required TimeSpan Duration { get; init; }

	public required CollectionOutcome Outcome { get; init; }

	public required int HostsDiscovered { get; init; }

	public required int HostsWithIp { get; init; }

	/// <summary>Node-level failures (unreachable, 401, TLS rejected).</summary>
	public IReadOnlyList<string> Errors { get; init; } = [];

	/// <summary>Guest-level failures (no agent, no address in the configured subnets).</summary>
	public IReadOnlyList<HostIssue> Issues { get; init; } = [];
}
