namespace Proxmox.Fqdn.Exporter.Options;

/// <summary>
///     Configuration for export options.
/// </summary>
public class Export
{
	/// <summary>
	///     Indicates whether to export to console.
	/// </summary>
	public bool Console { get; set; }

	/// <summary>
	///     DNS provider export configuration.
	/// </summary>
	public Dns? Dns { get; set; }

	/// <summary>
	///     Pihole export configuration. Null if not used.
	/// </summary>
	public Pihole? Pihole { get; set; }
}
