namespace ProxmoxIpMonitor.Abstractions.Models;

public enum DnsRecordAction
{
	/// <summary>Record already matched the desired state and carried our marker.</summary>
	Skipped,

	/// <summary>Record was created or updated.</summary>
	Written,

	/// <summary>Record carried our marker but no longer has a host behind it.</summary>
	Deleted,

	/// <summary>The provider rejected the operation.</summary>
	Failed
}

public sealed record DnsRecordOutcome
{
	public required string Domain { get; init; }

	public string? Ip { get; init; }

	public required DnsRecordAction Action { get; init; }

	public string? Error { get; init; }
}

/// <summary>One reconciliation pass against a DNS provider.</summary>
public sealed record DnsPush
{
	public string? Id { get; init; }

	public required string Provider { get; init; }

	public required DateTime At { get; init; }

	public required TimeSpan Duration { get; init; }

	/// <summary>True when the pass only computed the diff, because reconciliation is disabled.</summary>
	public required bool DryRun { get; init; }

	public required int Written { get; init; }

	public required int Skipped { get; init; }

	public required int Deleted { get; init; }

	public required int Failed { get; init; }

	public IReadOnlyList<DnsRecordOutcome> Outcomes { get; init; } = [];

	public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>A single A record the backend intends the zone to hold.</summary>
public sealed record DesiredRecord(string Domain, string Ip);

/// <summary>An A record currently in the zone.</summary>
public sealed record ExistingRecord(string Domain, string Ip, int Ttl, string? Comments);

/// <summary>
///     Comparison between what the backend wants and what the zone actually holds.
///     <see cref="Unmanaged" /> exists to make it visible that manually created records are seen
///     and deliberately left alone.
/// </summary>
public sealed record DnsState
{
	public required bool Enabled { get; init; }

	public required string Zone { get; init; }

	public IReadOnlyList<DesiredRecord> UpToDate { get; init; } = [];

	public IReadOnlyList<DesiredRecord> ToWrite { get; init; } = [];

	/// <summary>Records carrying our marker with no host behind them any more.</summary>
	public IReadOnlyList<ExistingRecord> Orphans { get; init; } = [];

	/// <summary>Records without our marker. Never written, never deleted.</summary>
	public IReadOnlyList<ExistingRecord> Unmanaged { get; init; } = [];

	public string? Error { get; init; }
}
