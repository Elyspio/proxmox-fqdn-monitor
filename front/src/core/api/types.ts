// Mirrors the backend JSON. Enums are serialised by name (JsonStringEnumConverter),
// so these unions must stay in step with the C# enums they shadow.

export type HostType = "Node" | "Vm" | "Container";

export interface MonitoredHost {
	key: string;
	nodeId: string;
	nodeName: string;
	type: HostType;
	vmId: number;
	hostname: string;
	ip: string | null;
	/** VLAN tag of the NIC carrying `ip`; null for the hypervisor or an untagged NIC. */
	vlan: number | null;
	firstSeenAt: string;
	lastSeenAt: string;
	present: boolean;
	excluded: boolean;
	pinned: boolean;
}

export type IpEventKind = "Appeared" | "Changed" | "Disappeared" | "Renamed";

export interface IpEvent {
	id: string;
	hostKey: string;
	hostname: string;
	type: HostType;
	nodeName: string;
	kind: IpEventKind;
	previousIp: string | null;
	currentIp: string | null;
	at: string;
}

export interface Page<T> {
	items: T[];
	total: number;
}

export type CollectionOutcome = "Succeeded" | "Partial" | "Failed";

export interface HostIssue {
	vmId: number;
	hostname: string;
	type: HostType;
	reason: string;
}

export interface CollectionRun {
	id: string;
	nodeId: string;
	nodeName: string;
	startedAt: string;
	duration: string;
	outcome: CollectionOutcome;
	hostsDiscovered: number;
	hostsWithIp: number;
	errors: string[];
	issues: HostIssue[];
}

export type DnsRecordAction = "Skipped" | "Written" | "Deleted" | "Failed";

export interface DnsRecordOutcome {
	domain: string;
	ip: string | null;
	action: DnsRecordAction;
	error: string | null;
}

export interface DnsPush {
	id: string;
	provider: string;
	at: string;
	duration: string;
	dryRun: boolean;
	written: number;
	skipped: number;
	deleted: number;
	failed: number;
	outcomes: DnsRecordOutcome[];
	errors: string[];
}

export interface DesiredRecord {
	domain: string;
	ip: string;
}

export interface ExistingRecord {
	domain: string;
	ip: string;
	ttl: number;
	comments: string | null;
}

export interface DnsState {
	enabled: boolean;
	zone: string;
	upToDate: DesiredRecord[];
	toWrite: DesiredRecord[];
	/** Records this tool owns that no longer have a host behind them. */
	orphans: ExistingRecord[];
	/** Records it did not write. Displayed so it is visible they are deliberately untouched. */
	unmanaged: ExistingRecord[];
	error: string | null;
}

export interface NodeDto {
	id: string;
	displayName: string;
	baseUrl: string;
	nodeName: string;
	tokenId: string;
	/** The secret itself is never sent to the browser. */
	hasToken: boolean;
	allowInvalidCertificate: boolean;
	enabled: boolean;
}

export interface NodeWriteDto {
	displayName: string;
	baseUrl: string;
	nodeName: string;
	tokenId: string;
	/** Empty means "keep the stored secret". */
	tokenSecret?: string;
	allowInvalidCertificate: boolean;
	enabled: boolean;
}

export interface TechnitiumDto {
	enabled: boolean;
	baseUrl: string;
	hasApiToken: boolean;
	zone: string;
	primaryNode: string | null;
	recordTtlSeconds: number;
	createPtr: boolean;
}

/** A configured subnet: the CIDR, plus an optional human name for the VLAN it maps to. */
export interface Subnet {
	cidr: string;
	label: string | null;
}

export interface SettingsDto {
	pollInterval: string;
	subnetsFilter: Subnet[];
	retentionMinutes: number;
	excludedHostnames: string[];
	reconciliationEnabled: boolean;
	deleteOrphanRecords: boolean;
	journalRetentionDays: number;
	technitium: TechnitiumDto;
}

export interface SettingsWriteDto {
	pollInterval: string;
	subnetsFilter: Subnet[];
	retentionMinutes: number;
	excludedHostnames: string[];
	reconciliationEnabled: boolean;
	deleteOrphanRecords: boolean;
	journalRetentionDays: number;
	technitium: {
		enabled: boolean;
		baseUrl: string;
		apiToken?: string;
		zone: string;
		primaryNode: string | null;
		recordTtlSeconds: number;
		createPtr: boolean;
	};
}

export interface TestResult {
	success: boolean;
	message: string;
}
