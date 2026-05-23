namespace Proxmox.Fqdn.Exporter.Options;

/// <summary>
///     Technitium DNS API export configuration.
/// </summary>
public class Technitium
{
	/// <summary>
	///     Technitium primary node API base URL, for example http://ely-dns-01.elylan:5380.
	/// </summary>
	public string BaseUrl { get; set; } = string.Empty;

	/// <summary>
	///     Non-expiring Technitium API token with permission to modify the target zone.
	/// </summary>
	public string ApiToken { get; set; } = string.Empty;

	/// <summary>
	///     Authoritative zone to update, for example elylan, rama, or toto.
	/// </summary>
	public string Zone { get; set; } = string.Empty;

	/// <summary>
	///     Optional Technitium cluster primary node domain. Updates should target the primary node.
	/// </summary>
	public string? PrimaryNode { get; set; }

	/// <summary>
	///     DNS record TTL in seconds.
	/// </summary>
	public int RecordTtlSeconds { get; set; } = 300;

	/// <summary>
	///     Optional record expiry TTL in seconds. When set, stale records are automatically removed by Technitium.
	/// </summary>
	public int? RecordExpirySeconds { get; set; }

	/// <summary>
	///     Indicates whether Technitium should create PTR records for exported A records.
	/// </summary>
	public bool CreatePtr { get; set; }
}
