namespace Proxmox.Fqdn.Exporter.Options;

/// <summary>
///     DNS export configuration.
/// </summary>
public class Dns
{
	/// <summary>
	///     Technitium DNS provider configuration.
	/// </summary>
	public Technitium? Technitium { get; set; }
}
