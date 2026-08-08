import { describe, expect, it } from "vitest";
import type { MonitoredHost, Subnet } from "@/core/api/types";
import { buildVlanGroups, cidrPrefix, ipInCidr, matchSubnet, suggestCidr } from "./vlan";

function subnet(cidr: string, label: string | null = null): Subnet {
	return { cidr, label };
}

function guest(
	hostname: string,
	ip: string | null,
	vlan: number | null,
	type: MonitoredHost["type"] = "Vm",
): MonitoredHost {
	return {
		key: `node:${type}:${hostname}`,
		nodeId: "node",
		nodeName: "proxmox",
		type,
		vmId: 100,
		hostname,
		ip,
		vlan,
		firstSeenAt: "2026-01-01T00:00:00Z",
		lastSeenAt: "2026-01-01T00:00:00Z",
		present: true,
		excluded: false,
		pinned: false,
	};
}

describe("ipInCidr", () => {
	it("matches an address inside the range", () => {
		expect(ipInCidr("10.0.0.0/24", "10.0.0.240")).toBe(true);
		expect(ipInCidr("10.1.0.0/24", "10.1.0.41")).toBe(true);
	});

	it("rejects an address outside the range", () => {
		expect(ipInCidr("10.0.0.0/24", "10.0.1.5")).toBe(false);
	});

	it("treats /0 as matching everything and rejects malformed input", () => {
		expect(ipInCidr("0.0.0.0/0", "8.8.8.8")).toBe(true);
		expect(ipInCidr("10.0.0.0/33", "10.0.0.1")).toBe(false);
		expect(ipInCidr("10.0.0.0/24", "not-an-ip")).toBe(false);
		expect(ipInCidr("10.0.0.0/24", "10.0.0.999")).toBe(false);
	});
});

describe("cidrPrefix", () => {
	it("returns the prefix length, or null on malformed input", () => {
		expect(cidrPrefix("10.0.0.0/24")).toBe(24);
		expect(cidrPrefix("0.0.0.0/0")).toBe(0);
		expect(cidrPrefix("10.0.0.0")).toBeNull();
		expect(cidrPrefix("10.0.0.0/33")).toBeNull();
	});
});

describe("suggestCidr", () => {
	it("returns the tightest enclosing range for hosts inside one /24", () => {
		// Smallest enclosing of .20/.240 is /24, and the clamp leaves /24 untouched.
		expect(suggestCidr(["10.0.0.20", "10.0.0.240"])).toBe("10.0.0.0/24");
	});

	it("never suggests narrower than /24, even for a single host", () => {
		// A lone .10 would enclose at /32; the clamp widens it to /24 so a powered-off sibling survives.
		expect(suggestCidr(["192.168.5.10"])).toBe("192.168.5.0/24");
		expect(suggestCidr(["192.168.5.10", "192.168.5.14"])).toBe("192.168.5.0/24");
	});

	it("widens past /24 when hosts genuinely span several /24s", () => {
		expect(suggestCidr(["10.1.0.5", "10.1.1.5"])).toBe("10.1.0.0/23");
		expect(suggestCidr(["10.1.0.5", "10.1.3.5"])).toBe("10.1.0.0/22");
	});

	it("normalises to the network address and copes with high-bit octets", () => {
		expect(suggestCidr(["200.0.0.9", "200.0.0.200"])).toBe("200.0.0.0/24");
	});

	it("ignores unparseable and IPv6 entries, returning null when nothing is left", () => {
		expect(suggestCidr(["10.0.0.5", null, "not-an-ip"])).toBe("10.0.0.0/24");
		expect(suggestCidr([null, "fe80::1"])).toBeNull();
		expect(suggestCidr([])).toBeNull();
	});
});

describe("matchSubnet", () => {
	const subnets = [subnet("10.0.0.0/8"), subnet("10.0.1.0/24", "Cluster K8s")];

	it("picks the most specific (longest-prefix) subnet", () => {
		expect(matchSubnet(subnets, "10.0.1.41")?.cidr).toBe("10.0.1.0/24");
	});

	it("falls back to the broader subnet when no narrower one matches", () => {
		expect(matchSubnet(subnets, "10.5.5.5")?.cidr).toBe("10.0.0.0/8");
	});

	it("returns null for a null IP or no match", () => {
		expect(matchSubnet(subnets, null)).toBeNull();
		expect(matchSubnet([subnet("10.0.0.0/24")], "192.168.1.1")).toBeNull();
	});
});

describe("buildVlanGroups", () => {
	const subnets = [subnet("10.0.0.0/24", "Services"), subnet("10.1.0.0/24", "Données")];

	it("groups by the real NIC tag, ascending, naming each group from a matched subnet", () => {
		const groups = buildVlanGroups(
			[
				guest("db-node-01", "10.1.0.41", 100),
				guest("dns", "10.0.0.240", 0),
				guest("proxy", "10.0.0.20", 0),
			],
			subnets,
		);

		expect(groups.map((g) => g.vlan)).toEqual([0, 100]);
		expect(groups[0].subnet?.label).toBe("Services");
		expect(groups[1].subnet?.label).toBe("Données");
		// Within a group, hosts sort by IP numerically (…20 before …240).
		expect(groups[0].hosts.map((h) => h.hostname)).toEqual(["proxy", "dns"]);
	});

	it("leaves the subnet null for a tagged host whose IP matches no configured subnet", () => {
		const groups = buildVlanGroups([guest("stray", "192.168.9.9", 7)], subnets);

		expect(groups[0].vlan).toBe(7);
		expect(groups[0].subnet).toBeNull();
	});

	it("puts a tagless host with an IP in an 'untagged' group", () => {
		const groups = buildVlanGroups([guest("native", "10.0.0.5", null)], subnets);

		expect(groups.find((g) => g.kind === "untagged")?.hosts.map((h) => h.hostname)).toEqual([
			"native",
		]);
	});

	it("puts an IP-less host in 'no-address', after the numbered and untagged groups", () => {
		const groups = buildVlanGroups(
			[
				guest("broken", null, null),
				guest("native", "10.0.0.5", null),
				guest("dns", "10.0.0.240", 0),
			],
			subnets,
		);

		expect(groups.map((g) => g.kind)).toEqual(["vlan", "untagged", "no-address"]);
		expect(groups.at(-1)?.hosts.map((h) => h.hostname)).toEqual(["broken"]);
	});
});
