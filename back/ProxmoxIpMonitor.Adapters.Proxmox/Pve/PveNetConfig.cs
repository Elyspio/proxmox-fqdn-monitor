using System.Text.RegularExpressions;

namespace ProxmoxIpMonitor.Adapters.Proxmox.Pve;

/// <summary>
///     Extracts the VLAN of a guest from its Proxmox NIC configuration. Proxmox stores each NIC as a
///     comma-separated option string keyed <c>net0</c>, <c>net1</c>, ... — for a VM
///     <c>"virtio=BC:24:11:AA:BB:CC,bridge=vmbr0,tag=30"</c>, for a container
///     <c>"name=eth0,bridge=vmbr0,hwaddr=BC:24:11:AA:BB:CC,tag=30"</c>. The VLAN is the <c>tag=</c>
///     value; an untagged NIC (native VLAN) has no tag.
/// </summary>
internal static partial class PveNetConfig
{
	/// <summary>True for the config keys that hold a NIC definition: net0, net1, ...</summary>
	public static bool IsNetKey(string key)
	{
		return NetKeyRegex().IsMatch(key);
	}

	/// <summary>
	///     Returns the VLAN of the NIC that carries the observed address, matched by <paramref name="mac" />.
	///     Falls back to the primary NIC (lowest index) when the MAC cannot be matched — a guest whose
	///     address could not be tied to a specific interface. Null when the chosen NIC is untagged.
	/// </summary>
	public static int? ResolveVlan(IReadOnlyDictionary<string, string> nets, string? mac)
	{
		if (nets.Count == 0) return null;

		if (!string.IsNullOrWhiteSpace(mac))
			foreach (var (_, definition) in nets)
				if (MacOf(definition) is { } nicMac && string.Equals(nicMac, mac, StringComparison.OrdinalIgnoreCase))
					return TagOf(definition);

		var primary = nets.OrderBy(net => IndexOf(net.Key)).First();
		return TagOf(primary.Value);
	}

	private static int? TagOf(string definition)
	{
		foreach (var token in definition.Split(','))
		{
			var parts = token.Split('=', 2);
			if (parts.Length == 2 && parts[0].Trim() == "tag" && int.TryParse(parts[1].Trim(), out var tag))
				return tag;
		}

		return null;
	}

	private static string? MacOf(string definition)
	{
		// The MAC is the value of the NIC model option (VM: "virtio=MAC") or the explicit hwaddr
		// option (container: "hwaddr=MAC"); either way it surfaces as a value shaped like a MAC.
		foreach (var token in definition.Split(','))
		{
			var parts = token.Split('=', 2);
			if (parts.Length == 2 && MacRegex().IsMatch(parts[1].Trim()))
				return parts[1].Trim();
		}

		return null;
	}

	private static int IndexOf(string netKey)
	{
		return int.TryParse(netKey.AsSpan(3), out var index) ? index : int.MaxValue;
	}

	[GeneratedRegex(@"^net\d+$")]
	private static partial Regex NetKeyRegex();

	[GeneratedRegex(@"^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$")]
	private static partial Regex MacRegex();
}
