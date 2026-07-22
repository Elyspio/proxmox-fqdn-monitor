using System.Net;

namespace ProxmoxIpMonitor.Abstractions;

/// <summary>
///     Decides whether an address belongs to one of the configured CIDR ranges.
///     Ported from the original exporter's NetworkAdapter, minus its dependency on
///     IOptionsMonitor and ILogger so it stays a pure, directly testable function.
/// </summary>
public static class SubnetFilter
{
	/// <summary>Returns true when <paramref name="ip" /> falls inside any of <paramref name="cidrs" />.</summary>
	public static bool IsInAny(IEnumerable<string> cidrs, string ip)
	{
		return cidrs.Any(cidr => IsIn(cidr, ip));
	}

	/// <summary>Returns true when <paramref name="ip" /> falls inside the single CIDR range <paramref name="cidr" />.</summary>
	public static bool IsIn(string cidr, string ip)
	{
		// IPv6 is out of scope: the DNS export only ever writes A records.
		if (ip.Count(c => c == '.') != 3) return false;

		var parts = cidr.Split('/');
		if (parts.Length != 2) return false;
		if (!IPAddress.TryParse(parts[0], out var network)) return false;
		if (!IPAddress.TryParse(ip, out var address)) return false;
		if (!int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32) return false;

		var networkBytes = network.GetAddressBytes();
		var addressBytes = address.GetAddressBytes();
		if (networkBytes.Length != 4 || addressBytes.Length != 4) return false;

		var mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
		var net = BinaryPrimitivesToUInt32(networkBytes);
		var addr = BinaryPrimitivesToUInt32(addressBytes);

		return (addr & mask) == (net & mask);
	}

	private static uint BinaryPrimitivesToUInt32(byte[] bytes)
	{
		return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
	}
}
