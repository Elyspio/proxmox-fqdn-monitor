namespace ProxmoxIpMonitor.Abstractions.Models;

/// <summary>
///     A configured subnet. The <see cref="Cidr" /> decides which address is kept for a guest during
///     collection and, on the UI, supplies the CIDR and human name for the VLAN a host sits in — the
///     VLAN number itself comes from the guest's real NIC tag (<see cref="MonitoredHost.Vlan" />), not
///     from configuration. <see cref="Label" /> is optional; a subnet is fully usable with just a CIDR,
///     which is also how legacy settings documents (a bare CIDR string) load.
/// </summary>
public sealed record Subnet
{
	public required string Cidr { get; init; }

	/// <summary>Optional human name, e.g. "Services". Absent means the group shows its raw CIDR.</summary>
	public string? Label { get; init; }
}
