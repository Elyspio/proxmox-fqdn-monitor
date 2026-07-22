import { useMemo, useState } from "react";
import {
	Chip,
	IconButton,
	MenuItem,
	Paper,
	Stack,
	Table,
	TableBody,
	TableCell,
	TableContainer,
	TableHead,
	TableRow,
	TableSortLabel,
	TextField,
	Tooltip,
	Typography,
} from "@mui/material";
import PushPinIcon from "@mui/icons-material/PushPin";
import PushPinOutlinedIcon from "@mui/icons-material/PushPinOutlined";
import BlockIcon from "@mui/icons-material/Block";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutlineOutlined";
import { useHosts } from "@/core/api/queries";
import { usePatchHost } from "@/core/api/mutations";
import type { MonitoredHost } from "@/core/api/types";
import {
	DateTimeText,
	Empty,
	HostTypeChip,
	Loading,
	Mono,
	PageTitle,
	QueryError,
	SpacedRow,
} from "@/view/components/Common";

type SortKey = "hostname" | "ip" | "type" | "nodeName" | "vmId" | "vlan" | "lastSeenAt";
type SortOrder = "asc" | "desc";

/** Filter sentinels kept out of the numeric VLAN space so they never collide with a real tag. */
const VLAN_ALL = "all";
const VLAN_NONE = "none";

/** Packs an IPv4 into one number so addresses sort numerically, not lexically (…2 before …10). */
function ipToSortable(ip: string | null): number {
	if (!ip) return -1;
	const parts = ip.split(".");
	if (parts.length !== 4) return -1;
	let value = 0;
	for (const part of parts) {
		const octet = Number(part);
		if (!Number.isInteger(octet)) return -1;
		value = value * 256 + octet;
	}
	return value;
}

/** Comparable key per column. Missing values (no IP, no VLAN, a node's absent VMID) sort first. */
function sortValue(host: MonitoredHost, key: SortKey): number | string {
	switch (key) {
		case "hostname":
			return host.hostname.toLowerCase();
		case "ip":
			return ipToSortable(host.ip);
		case "type":
			return host.type;
		case "nodeName":
			return host.nodeName.toLowerCase();
		case "vmId":
			return host.type === "Node" ? -1 : host.vmId;
		case "vlan":
			return host.vlan ?? -1;
		case "lastSeenAt":
			return new Date(host.lastSeenAt).getTime();
	}
}

function compare(a: MonitoredHost, b: MonitoredHost, key: SortKey): number {
	const av = sortValue(a, key);
	const bv = sortValue(b, key);
	if (av < bv) return -1;
	if (av > bv) return 1;
	return 0;
}

/** Stable sort: the original order breaks ties, so rows do not jitter between renders. */
function sortRows(rows: MonitoredHost[], key: SortKey, order: SortOrder): MonitoredHost[] {
	return rows
		.map((row, index) => [row, index] as const)
		.sort((a, b) => {
			const cmp = compare(a[0], b[0], key);
			return cmp !== 0 ? (order === "asc" ? cmp : -cmp) : a[1] - b[1];
		})
		.map(([row]) => row);
}

export function HostsPage() {
	const hosts = useHosts();
	const patch = usePatchHost();
	const [filter, setFilter] = useState("");
	const [vlanFilter, setVlanFilter] = useState<string>(VLAN_ALL);
	const [orderBy, setOrderBy] = useState<SortKey>("hostname");
	const [order, setOrder] = useState<SortOrder>("asc");

	// The VLANs the filter can offer are whatever the current snapshot actually contains.
	const vlanOptions = useMemo(() => {
		const values = new Set<number>();
		let hasUntagged = false;
		for (const host of hosts.data ?? []) {
			if (host.vlan === null) hasUntagged = true;
			else values.add(host.vlan);
		}
		return { values: [...values].sort((a, b) => a - b), hasUntagged };
	}, [hosts.data]);

	const rows = useMemo(() => {
		const needle = filter.trim().toLowerCase();
		let all = hosts.data ?? [];
		if (needle) {
			all = all.filter(
				(host) =>
					host.hostname.toLowerCase().includes(needle) ||
					(host.ip ?? "").includes(needle) ||
					host.nodeName.toLowerCase().includes(needle),
			);
		}
		if (vlanFilter !== VLAN_ALL) {
			all = all.filter((host) => (vlanFilter === VLAN_NONE ? host.vlan === null : host.vlan === Number(vlanFilter)));
		}
		return sortRows(all, orderBy, order);
	}, [hosts.data, filter, vlanFilter, orderBy, order]);

	if (hosts.isLoading) return <Loading />;
	if (hosts.isError) return <QueryError error={hosts.error} />;

	const handleRequestSort = (column: SortKey) => {
		if (orderBy === column) {
			setOrder((prev) => (prev === "asc" ? "desc" : "asc"));
		} else {
			setOrderBy(column);
			setOrder("asc");
		}
	};

	const sortHeader = (column: SortKey, label: string, align?: "right") => (
		<TableCell align={align} sortDirection={orderBy === column ? order : false}>
			<TableSortLabel
				active={orderBy === column}
				direction={orderBy === column ? order : "asc"}
				onClick={() => handleRequestSort(column)}
			>
				{label}
			</TableSortLabel>
		</TableCell>
	);

	return (
		<Stack spacing={3}>
			<SpacedRow>
				<PageTitle>Hôtes</PageTitle>
				<Stack direction="row" spacing={2}>
					<TextField
						size="small"
						placeholder="Filtrer par nom, IP ou nœud"
						value={filter}
						onChange={(event) => setFilter(event.target.value)}
						sx={{ width: 320 }}
					/>
					<TextField
						select
						size="small"
						label="VLAN"
						value={vlanFilter}
						onChange={(event) => setVlanFilter(event.target.value)}
						sx={{ width: 160 }}
					>
						<MenuItem value={VLAN_ALL}>Tous</MenuItem>
						{vlanOptions.hasUntagged && <MenuItem value={VLAN_NONE}>Sans VLAN</MenuItem>}
						{vlanOptions.values.map((vlan) => (
							<MenuItem key={vlan} value={String(vlan)}>
								{vlan}
							</MenuItem>
						))}
					</TextField>
				</Stack>
			</SpacedRow>

			<TableContainer component={Paper}>
				<Table size="small">
					<TableHead>
						<TableRow>
							{sortHeader("hostname", "Nom")}
							{sortHeader("ip", "Adresse")}
							{sortHeader("type", "Type")}
							{sortHeader("nodeName", "Nœud")}
							{sortHeader("vmId", "VMID")}
							{sortHeader("vlan", "VLAN")}
							<TableCell>État</TableCell>
							{sortHeader("lastSeenAt", "Vu pour la dernière fois")}
							<TableCell align="right">Actions</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{rows.map((host) => (
							<TableRow key={host.key} hover>
								<TableCell>
									<Mono>{host.hostname}</Mono>
								</TableCell>
								<TableCell>
									{host.ip ? (
										<Mono>{host.ip}</Mono>
									) : (
										<Typography variant="body2" color="text.secondary">
											—
										</Typography>
									)}
								</TableCell>
								<TableCell>
									<HostTypeChip type={host.type} />
								</TableCell>
								<TableCell>{host.nodeName}</TableCell>
								<TableCell>{host.type === "Node" ? "—" : host.vmId}</TableCell>
								<TableCell>{host.vlan ?? "—"}</TableCell>
								<TableCell>
									<Stack direction="row" spacing={1}>
										{host.present ? (
											<Chip size="small" color="success" label="Actif" />
										) : (
											// Retained past its last sighting: still resolvable until retention expires.
											<Chip size="small" color="warning" variant="outlined" label="Rétention" />
										)}
										{host.excluded && <Chip size="small" label="Exclu du DNS" />}
										{host.pinned && <Chip size="small" label="Épinglé" />}
									</Stack>
								</TableCell>
								<TableCell>
									<DateTimeText value={host.lastSeenAt} />
								</TableCell>
								<TableCell align="right">
									<Tooltip title={host.pinned ? "Ne plus épingler" : "Épingler (survit à la rétention)"}>
										<IconButton size="small" onClick={() => patch.mutate({ key: host.key, pinned: !host.pinned })}>
											{host.pinned ? <PushPinIcon fontSize="small" /> : <PushPinOutlinedIcon fontSize="small" />}
										</IconButton>
									</Tooltip>
									<Tooltip title={host.excluded ? "Réintégrer au DNS" : "Exclure du DNS"}>
										<IconButton size="small" onClick={() => patch.mutate({ key: host.key, excluded: !host.excluded })}>
											{host.excluded ? <CheckCircleOutlineIcon fontSize="small" /> : <BlockIcon fontSize="small" />}
										</IconButton>
									</Tooltip>
								</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
				{rows.length === 0 && (
					<Empty>Aucun hôte. Déclarez un nœud Proxmox dans les réglages, puis lancez une collecte.</Empty>
				)}
			</TableContainer>
		</Stack>
	);
}
