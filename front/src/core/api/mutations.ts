import { useMutation, useQueryClient } from "@tanstack/react-query";
import { http } from "./client";
import { qk } from "./queries";
import type { DnsPush, MonitoredHost, NodeDto, NodeWriteDto, SettingsDto, SettingsWriteDto, TestResult } from "./types";

/** Forcing a collection changes the snapshot, the history, the health and the DNS diff. */
export function useCollectNow() {
	const qc = useQueryClient();
	return useMutation({
		mutationFn: async () => (await http.post("/api/collect")).data,
		onSuccess: () => {
			void qc.invalidateQueries({ queryKey: qk.hosts });
			void qc.invalidateQueries({ queryKey: qk.collectionHealth });
			void qc.invalidateQueries({ queryKey: qk.dnsState });
			void qc.invalidateQueries({ queryKey: ["events"] });
		},
	});
}

export function usePushDns() {
	const qc = useQueryClient();
	return useMutation({
		mutationFn: async () => (await http.post<DnsPush[]>("/api/dns/push")).data,
		onSuccess: () => {
			void qc.invalidateQueries({ queryKey: qk.dnsState });
			void qc.invalidateQueries({ queryKey: qk.dnsPushes });
		},
	});
}

export function usePatchHost() {
	const qc = useQueryClient();
	return useMutation({
		mutationFn: async (input: { key: string; pinned?: boolean; excluded?: boolean }) =>
			(await http.patch<MonitoredHost>(`/api/hosts/${encodeURIComponent(input.key)}`, input)).data,
		onSuccess: () => {
			void qc.invalidateQueries({ queryKey: qk.hosts });
			// Excluding a host changes what the zone should hold.
			void qc.invalidateQueries({ queryKey: qk.dnsState });
		},
	});
}

export function useSaveSettings() {
	const qc = useQueryClient();
	return useMutation({
		mutationFn: async (body: SettingsWriteDto) => (await http.put<SettingsDto>("/api/settings", body)).data,
		onSuccess: () => {
			void qc.invalidateQueries({ queryKey: qk.settings });
			void qc.invalidateQueries({ queryKey: qk.dnsState });
		},
	});
}

export function useCreateNode() {
	const qc = useQueryClient();
	return useMutation({
		mutationFn: async (body: NodeWriteDto) => (await http.post<NodeDto>("/api/nodes", body)).data,
		onSuccess: () => void qc.invalidateQueries({ queryKey: qk.nodes }),
	});
}

export function useUpdateNode() {
	const qc = useQueryClient();
	return useMutation({
		mutationFn: async (input: { id: string; body: NodeWriteDto }) =>
			(await http.put<NodeDto>(`/api/nodes/${input.id}`, input.body)).data,
		onSuccess: () => void qc.invalidateQueries({ queryKey: qk.nodes }),
	});
}

export function useDeleteNode() {
	const qc = useQueryClient();
	return useMutation({
		mutationFn: async (id: string) => (await http.delete(`/api/nodes/${id}`)).data,
		onSuccess: () => {
			void qc.invalidateQueries({ queryKey: qk.nodes });
			// Deleting a node drops its hosts with it.
			void qc.invalidateQueries({ queryKey: qk.hosts });
			void qc.invalidateQueries({ queryKey: qk.collectionHealth });
		},
	});
}

/** Probes a node's URL, TLS policy and token before it is committed. */
export function useTestNode() {
	return useMutation({
		mutationFn: async (input: { id?: string; body: NodeWriteDto }) =>
			(await http.post<TestResult>("/api/nodes/test", input.body, { params: { id: input.id } })).data,
	});
}
