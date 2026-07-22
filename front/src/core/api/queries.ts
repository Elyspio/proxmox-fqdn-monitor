import { useQuery } from "@tanstack/react-query";
import { http } from "./client";
import type {
	CollectionRun,
	DnsPush,
	DnsState,
	IpEvent,
	IpEventKind,
	MonitoredHost,
	NodeDto,
	Page,
	SettingsDto,
} from "./types";

/** Centralised query keys, so mutations and manual actions can invalidate precisely. */
export const qk = {
	hosts: ["hosts"] as const,
	events: (hostKey?: string, kind?: IpEventKind, skip = 0) => ["events", hostKey ?? null, kind ?? null, skip] as const,
	collectionHealth: ["health", "collection"] as const,
	dnsState: ["dns", "state"] as const,
	dnsPushes: ["dns", "pushes"] as const,
	settings: ["settings"] as const,
	nodes: ["nodes"] as const,
};

/**
 * Poll cadence for the live screens. There is no push channel by design: the data only
 * changes when a collection cycle runs, so a periodic refetch is enough and costs far less
 * than a realtime connection to maintain.
 */
const LIVE_REFETCH_MS = 30_000;

export function useHosts() {
	return useQuery({
		queryKey: qk.hosts,
		queryFn: async () => (await http.get<MonitoredHost[]>("/api/hosts")).data,
		refetchInterval: LIVE_REFETCH_MS,
	});
}

export function useEvents(hostKey?: string, kind?: IpEventKind, skip = 0, take = 100) {
	return useQuery({
		queryKey: qk.events(hostKey, kind, skip),
		queryFn: async () => {
			const { data } = await http.get<Page<IpEvent>>("/api/events", {
				params: { hostKey, kind, skip, take },
			});
			return data;
		},
	});
}

export function useCollectionHealth() {
	return useQuery({
		queryKey: qk.collectionHealth,
		queryFn: async () => (await http.get<CollectionRun[]>("/api/health/collection")).data,
		refetchInterval: LIVE_REFETCH_MS,
	});
}

export function useDnsState() {
	return useQuery({
		queryKey: qk.dnsState,
		queryFn: async () => (await http.get<DnsState[]>("/api/dns/state")).data,
	});
}

export function useDnsPushes(take = 20) {
	return useQuery({
		queryKey: qk.dnsPushes,
		queryFn: async () => (await http.get<DnsPush[]>("/api/dns/pushes", { params: { take } })).data,
	});
}

export function useSettings() {
	return useQuery({
		queryKey: qk.settings,
		queryFn: async () => (await http.get<SettingsDto>("/api/settings")).data,
	});
}

export function useNodes() {
	return useQuery({
		queryKey: qk.nodes,
		queryFn: async () => (await http.get<NodeDto[]>("/api/nodes")).data,
	});
}
