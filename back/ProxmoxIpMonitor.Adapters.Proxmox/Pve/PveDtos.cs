using System.Text.Json.Serialization;

namespace ProxmoxIpMonitor.Adapters.Proxmox.Pve;

/// <summary>Proxmox wraps every payload in a "data" envelope.</summary>
internal sealed class PveResponse<T>
{
	[JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>Entry of /nodes/{node}/qemu and /nodes/{node}/lxc.</summary>
internal sealed class PveGuest
{
	[JsonPropertyName("vmid")] public int VmId { get; set; }

	[JsonPropertyName("name")] public string? Name { get; set; }

	[JsonPropertyName("status")] public string? Status { get; set; }
}

/// <summary>Entry of /nodes.</summary>
internal sealed class PveNodeEntry
{
	[JsonPropertyName("node")] public string? Node { get; set; }

	[JsonPropertyName("status")] public string? Status { get; set; }
}

/// <summary>Entry of /nodes/{node}/network.</summary>
internal sealed class PveNetworkInterface
{
	[JsonPropertyName("iface")] public string? Iface { get; set; }

	[JsonPropertyName("address")] public string? Address { get; set; }

	[JsonPropertyName("cidr")] public string? Cidr { get; set; }

	[JsonPropertyName("active")] public int? Active { get; set; }
}

/// <summary>Entry of /nodes/{node}/lxc/{vmid}/interfaces.</summary>
internal sealed class PveLxcInterface
{
	[JsonPropertyName("name")] public string? Name { get; set; }

	[JsonPropertyName("inet")] public string? Inet { get; set; }

	[JsonPropertyName("hwaddr")] public string? HwAddr { get; set; }
}

/// <summary>Payload of /nodes/{node}/qemu/{vmid}/agent/network-get-interfaces.</summary>
internal sealed class PveAgentInterfaces
{
	[JsonPropertyName("result")] public List<PveAgentInterface>? Result { get; set; }
}

internal sealed class PveAgentInterface
{
	[JsonPropertyName("name")] public string? Name { get; set; }

	[JsonPropertyName("hardware-address")] public string? HardwareAddress { get; set; }

	[JsonPropertyName("ip-addresses")] public List<PveAgentIpAddress>? IpAddresses { get; set; }
}

internal sealed class PveAgentIpAddress
{
	[JsonPropertyName("ip-address")] public string? IpAddress { get; set; }

	[JsonPropertyName("ip-address-type")] public string? IpAddressType { get; set; }
}
