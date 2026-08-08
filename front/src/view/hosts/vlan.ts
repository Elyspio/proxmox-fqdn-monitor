import type { MonitoredHost, Subnet } from "@/core/api/types";

// Hosts are grouped by their real NIC VLAN tag (MonitoredHost.vlan, resolved by the backend). The
// configured subnets only supply the display CIDR and human name for a group, matched via the host IP
// — the number is never re-derived or configured by hand. IPv4 only, mirroring the backend.

interface ParsedCidr {
	network: number;
	prefix: number;
}

function toUint32(ip: string): number | null {
	const parts = ip.split(".");
	if (parts.length !== 4) return null;
	let value = 0;
	for (const part of parts) {
		if (!/^\d{1,3}$/.test(part)) return null;
		const octet = Number(part);
		if (octet > 255) return null;
		value = value * 256 + octet;
	}
	return value >>> 0;
}

function parseCidr(cidr: string): ParsedCidr | null {
	const [addr, prefixStr, ...rest] = cidr.trim().split("/");
	if (prefixStr === undefined || rest.length > 0) return null;
	const prefix = Number(prefixStr);
	if (!Number.isInteger(prefix) || prefix < 0 || prefix > 32) return null;
	const network = toUint32(addr);
	if (network === null) return null;
	return { network, prefix };
}

/** True when an IPv4 address falls inside a CIDR range. */
export function ipInCidr(cidr: string, ip: string): boolean {
	const parsed = parseCidr(cidr);
	const addr = toUint32(ip);
	if (parsed === null || addr === null) return false;
	const mask = parsed.prefix === 0 ? 0 : (0xffffffff << (32 - parsed.prefix)) >>> 0;
	return (addr & mask) >>> 0 === (parsed.network & mask) >>> 0;
}

/** The most specific (longest-prefix) configured subnet containing the address, or null. */
export function matchSubnet(subnets: readonly Subnet[], ip: string | null): Subnet | null {
	if (!ip) return null;
	let best: Subnet | null = null;
	let bestPrefix = -1;
	for (const subnet of subnets) {
		const parsed = parseCidr(subnet.cidr);
		if (parsed === null || !ipInCidr(subnet.cidr, ip)) continue;
		if (parsed.prefix > bestPrefix) {
			best = subnet;
			bestPrefix = parsed.prefix;
		}
	}
	return best;
}

/** The prefix length of a CIDR (0–32), or null when it does not parse. */
export function cidrPrefix(cidr: string): number | null {
	return parseCidr(cidr)?.prefix ?? null;
}

/** Formats a 32-bit value as a dotted-quad IPv4 address. */
function toIpString(value: number): string {
	return [24, 16, 8, 0].map((shift) => (value >>> shift) & 0xff).join(".");
}

/**
 * Suggests the tightest CIDR that still encloses every given IPv4 address, clamped to a maximum
 * prefix of /24. The clamp matters: you usually only see the powered-on hosts of a VLAN, so fitting
 * the range exactly to them would leave a guest that is off tonight outside the suggestion — and once
 * applied, that guest's address would stop passing the collection filter. Returns null when nothing
 * parses (e.g. an empty group or IPv6-only hosts).
 */
export function suggestCidr(ips: readonly (string | null)[]): string | null {
	const values: number[] = [];
	for (const ip of ips) {
		const value = ip ? toUint32(ip) : null;
		if (value !== null) values.push(value);
	}
	if (values.length === 0) return null;

	// OR of every address XOR'd against the first isolates the bits that differ across the group;
	// clz32 then counts the identical leading bits, i.e. the smallest enclosing prefix.
	let diff = 0;
	for (const value of values) diff |= value ^ values[0];
	const prefix = Math.min(Math.clz32(diff >>> 0), 24);
	const mask = prefix === 0 ? 0 : (0xffffffff << (32 - prefix)) >>> 0;
	return `${toIpString((values[0] & mask) >>> 0)}/${prefix}`;
}

/** Orders hosts inside a group by IP (numeric, so …2 precedes …10), IP-less hosts last. */
function byIp(a: MonitoredHost, b: MonitoredHost): number {
	const av = a.ip ? toUint32(a.ip) : null;
	const bv = b.ip ? toUint32(b.ip) : null;
	if (av === null && bv === null) return a.hostname.localeCompare(b.hostname);
	if (av === null) return 1;
	if (bv === null) return -1;
	return av - bv || a.hostname.localeCompare(b.hostname);
}

export type VlanGroupKind = "vlan" | "untagged" | "no-address";

export interface VlanGroup {
	/** Stable key for React and the persisted collapse state. */
	id: string;
	kind: VlanGroupKind;
	/** The real NIC tag for a "vlan" group; null for the fallback groups. */
	vlan: number | null;
	/** Configured subnet matched from the group's IPs, supplying CIDR + name. May be null. */
	subnet: Subnet | null;
	hosts: MonitoredHost[];
}

/**
 * Buckets guests by their NIC VLAN tag. A guest with an IP but no tag (native/untagged VLAN) goes to
 * an "untagged" group; a guest with no IP at all goes to "no-address". Numbered groups sort ascending;
 * the two fallback groups follow, and are only emitted when non-empty. Nodes are handled by the page.
 */
export function buildVlanGroups(
	guests: readonly MonitoredHost[],
	subnets: readonly Subnet[],
): VlanGroup[] {
	const byVlan = new Map<number, MonitoredHost[]>();
	const untagged: MonitoredHost[] = [];
	const noAddress: MonitoredHost[] = [];

	for (const host of guests) {
		if (!host.ip) noAddress.push(host);
		else if (host.vlan === null) untagged.push(host);
		else (byVlan.get(host.vlan) ?? byVlan.set(host.vlan, []).get(host.vlan)!).push(host);
	}

	const groups: VlanGroup[] = [...byVlan.entries()]
		.sort(([a], [b]) => a - b)
		.map(([vlan, hosts]) => {
			hosts.sort(byIp);
			// One VLAN maps to one subnet in practice; take the name/CIDR from the first host that matches.
			const subnet =
				hosts
					.map((host) => matchSubnet(subnets, host.ip))
					.find((match) => match !== null) ?? null;
			return { id: `vlan:${vlan}`, kind: "vlan" as const, vlan, subnet, hosts };
		});

	if (untagged.length > 0) {
		untagged.sort(byIp);
		groups.push({
			id: "untagged",
			kind: "untagged",
			vlan: null,
			subnet: null,
			hosts: untagged,
		});
	}
	if (noAddress.length > 0) {
		noAddress.sort(byIp);
		groups.push({
			id: "no-address",
			kind: "no-address",
			vlan: null,
			subnet: null,
			hosts: noAddress,
		});
	}

	return groups;
}
