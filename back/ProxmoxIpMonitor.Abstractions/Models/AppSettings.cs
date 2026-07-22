namespace ProxmoxIpMonitor.Abstractions.Models;

/// <summary>Technitium primary-zone export settings.</summary>
public sealed record TechnitiumSettings
{
	public bool Enabled { get; init; }

	/// <summary>Web service base URL, e.g. http://ely-dns-01.elylan:5380.</summary>
	public string BaseUrl { get; init; } = "";

	/// <summary>API token, protected at rest. Never serialised to the API.</summary>
	public string ApiTokenProtected { get; init; } = "";

	/// <summary>Authoritative zone that receives the A records, e.g. elylan.</summary>
	public string Zone { get; init; } = "";

	/// <summary>Cluster primary that owns the zone. Writes go here only; replication is Technitium's job.</summary>
	public string? PrimaryNode { get; init; }

	public int RecordTtlSeconds { get; init; } = 300;

	public bool CreatePtr { get; init; }
}

/// <summary>
///     Single-document settings, editable from the UI. Everything here can change without a redeploy,
///     which is why the collector re-reads it on every cycle rather than binding it once at startup.
/// </summary>
public sealed record AppSettings
{
	public const string SingletonId = "settings";

	public string Id { get; init; } = SingletonId;

	/// <summary>How often every enabled node is polled.</summary>
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

	/// <summary>CIDR ranges an address must fall into to be considered usable.</summary>
	public IReadOnlyList<string> SubnetsFilter { get; init; } = ["10.0.0.0/8"];

	/// <summary>
	///     How long a host keeps its DNS record after Proxmox stops reporting it.
	///     Preserves the original exporter's behaviour: a rebooting VM must not lose its name.
	/// </summary>
	public int RetentionMinutes { get; init; } = 300;

	/// <summary>Hostnames never written to DNS, matched case-insensitively.</summary>
	public IReadOnlyList<string> ExcludedHostnames { get; init; } = [];

	/// <summary>
	///     When false the backend still computes the desired zone state and reports the diff,
	///     but writes nothing. Intended as the safe first run against a live zone.
	/// </summary>
	public bool ReconciliationEnabled { get; init; }

	/// <summary>
	///     When false, records that fell out of retention are reported as orphans but never deleted.
	///     Deletion only ever targets records carrying this tool's ownership marker.
	/// </summary>
	public bool DeleteOrphanRecords { get; init; }

	public TechnitiumSettings Technitium { get; init; } = new();

	/// <summary>Retention of the history, health and DNS push journals.</summary>
	public int JournalRetentionDays { get; init; } = 90;
}
