namespace ProxmoxIpMonitor.Abstractions.Models;

/// <summary>What kind of Proxmox object an address belongs to.</summary>
public enum HostType
{
	/// <summary>The hypervisor itself.</summary>
	Node,

	/// <summary>A QEMU virtual machine (address read through the guest agent).</summary>
	Vm,

	/// <summary>An LXC container.</summary>
	Container
}

/// <summary>
///     A host as last observed on a Proxmox node.
///     <see cref="Key" /> is stable across IP and hostname changes, which is what makes
///     change detection possible: renaming a VM is a different event from re-addressing it.
/// </summary>
public sealed record MonitoredHost
{
	/// <summary>Stable identity: node + object type + vmid. See <see cref="BuildKey" />.</summary>
	public required string Key { get; init; }

	/// <summary>Identifier of the <see cref="PveNode" /> this host was discovered on.</summary>
	public required string NodeId { get; init; }

	/// <summary>Display name of the node, denormalised so the UI does not need a join.</summary>
	public required string NodeName { get; init; }

	public required HostType Type { get; init; }

	/// <summary>Proxmox vmid. Zero for <see cref="HostType.Node" />, which has no vmid.</summary>
	public required int VmId { get; init; }

	/// <summary>Name reported by Proxmox — the VM/CT name, not necessarily the guest's own hostname.</summary>
	public required string Hostname { get; init; }

	/// <summary>First IPv4 matching the configured subnet filter, or null when none was found.</summary>
	public string? Ip { get; init; }

	/// <summary>
	///     VLAN tag of the NIC carrying <see cref="Ip" />, read from the guest's Proxmox NIC config.
	///     Null for the hypervisor, for an untagged (native VLAN) NIC, or when it could not be read.
	/// </summary>
	public int? Vlan { get; init; }

	public required DateTime FirstSeenAt { get; init; }

	/// <summary>Last collection that saw this host with an IP. Drives retention expiry.</summary>
	public required DateTime LastSeenAt { get; init; }

	/// <summary>True while the host is currently reported by Proxmox; false once it is only kept by retention.</summary>
	public required bool Present { get; init; }

	/// <summary>Excluded hosts are collected and displayed but never written to DNS.</summary>
	public bool Excluded { get; init; }

	/// <summary>Pinned hosts survive retention expiry and are never reconciled away.</summary>
	public bool Pinned { get; init; }

	public static string BuildKey(string nodeId, HostType type, int vmId)
	{
		return $"{nodeId}:{type}:{vmId}";
	}
}
