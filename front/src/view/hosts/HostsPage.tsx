import { type ReactNode, useCallback, useMemo, useState } from "react";
import {
	Box,
	Chip,
	Collapse,
	IconButton,
	Stack,
	TextField,
	Tooltip,
	Typography,
} from "@mui/material";
import PushPinIcon from "@mui/icons-material/PushPin";
import PushPinOutlinedIcon from "@mui/icons-material/PushPinOutlined";
import BlockIcon from "@mui/icons-material/Block";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutlineOutlined";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import DnsOutlinedIcon from "@mui/icons-material/DnsOutlined";
import { useHosts, useSettings } from "@/core/api/queries";
import { usePatchHost } from "@/core/api/mutations";
import type { MonitoredHost } from "@/core/api/types";
import { Empty, HostTypeChip, Loading, Mono, QueryError } from "@/view/components/Common";
import { buildVlanGroups, type VlanGroup } from "./vlan";

const COLLAPSE_KEY = "hosts.collapsedVlans";

// type | name | ip | vmid | status | actions — one grid so columns line up across every group.
const ROW_COLUMNS = "84px minmax(0, 1fr) 132px 52px minmax(128px, auto) 76px";

/** Remembers which groups the user collapsed, so the layout survives a reload. */
function usePersistentCollapse(key: string) {
	const [collapsed, setCollapsed] = useState<Set<string>>(() => {
		try {
			const raw = localStorage.getItem(key);
			return new Set<string>(raw ? (JSON.parse(raw) as string[]) : []);
		} catch {
			return new Set<string>();
		}
	});

	const toggle = useCallback(
		(id: string) => {
			setCollapsed((prev) => {
				const next = new Set(prev);
				if (next.has(id)) next.delete(id);
				else next.add(id);
				try {
					localStorage.setItem(key, JSON.stringify([...next]));
				} catch {
					// Private mode / disabled storage: fall back to in-memory state only.
				}
				return next;
			});
		},
		[key],
	);

	return { collapsed, toggle };
}

export function HostsPage() {
	const hosts = useHosts();
	const settings = useSettings();
	const patch = usePatchHost();
	const [filter, setFilter] = useState("");
	const { collapsed, toggle } = usePersistentCollapse(COLLAPSE_KEY);

	const needle = filter.trim().toLowerCase();

	const matches = useCallback(
		(host: MonitoredHost) =>
			!needle ||
			host.hostname.toLowerCase().includes(needle) ||
			(host.ip ?? "").includes(needle) ||
			host.nodeName.toLowerCase().includes(needle),
		[needle],
	);

	const all = hosts.data ?? [];
	const subnets = settings.data?.subnetsFilter ?? [];

	const nodes = useMemo(
		() => all.filter((host) => host.type === "Node").filter(matches),
		[all, matches],
	);
	const guests = useMemo(() => all.filter((host) => host.type !== "Node"), [all]);

	const counts = useMemo(
		() => ({
			vm: guests.filter((host) => host.type === "Vm").length,
			lxc: guests.filter((host) => host.type === "Container").length,
			active: guests.filter((host) => host.present).length,
		}),
		[guests],
	);

	// Bucket every guest by its real VLAN tag, then apply the search filter inside each group.
	const groups = useMemo(() => {
		return buildVlanGroups(guests, subnets)
			.map((group) => ({ ...group, hosts: group.hosts.filter(matches) }))
			.filter((group) => group.hosts.length > 0);
	}, [guests, subnets, matches]);

	if (hosts.isLoading || settings.isLoading) return <Loading />;
	if (hosts.isError) return <QueryError error={hosts.error} />;
	if (settings.isError) return <QueryError error={settings.error} />;

	const nothing = nodes.length === 0 && groups.length === 0;

	return (
		<Stack spacing={3}>
			<Box
				sx={{
					display: "flex",
					flexWrap: "wrap",
					justifyContent: "space-between",
					alignItems: "center",
					gap: 2,
				}}
			>
				<Stack direction="row" spacing={3} sx={{ flexWrap: "wrap" }}>
					<StatPill color="primary.main" count={counts.vm} label="VM" />
					<StatPill color="text.secondary" count={counts.lxc} label="LXC/PCT" />
					<StatPill color="success.main" count={counts.active} label="Actifs" />
				</Stack>
				<TextField
					size="small"
					placeholder="Filtrer par nom, IP ou nœud"
					value={filter}
					onChange={(event) => setFilter(event.target.value)}
					sx={{ width: 320, maxWidth: "100%" }}
				/>
			</Box>

			{nothing ? (
				needle ? (
					<Empty>Aucun hôte ne correspond au filtre.</Empty>
				) : (
					<Empty>
						Aucun hôte. Déclarez un nœud Proxmox dans les réglages, puis lancez une
						collecte.
					</Empty>
				)
			) : (
				<>
					{nodes.length > 0 && (
						<Box>
							<SectionLabel>Hyperviseur</SectionLabel>
							<Stack spacing={1}>
								{nodes.map((node) => (
									<HypervisorRow key={node.key} node={node} />
								))}
							</Stack>
						</Box>
					)}

					{groups.length > 0 && (
						<Box>
							<SectionLabel>VLANs</SectionLabel>
							<Stack spacing={1}>
								{groups.map((group) => (
									<VlanGroupBlock
										key={group.id}
										group={group}
										// While filtering, force every surviving group open regardless of remembered state.
										expanded={needle ? true : !collapsed.has(group.id)}
										onToggle={() => toggle(group.id)}
										onPatch={(input) => patch.mutate(input)}
									/>
								))}
							</Stack>
						</Box>
					)}
				</>
			)}
		</Stack>
	);
}

function StatPill({ color, count, label }: { color: string; count: number; label: string }) {
	return (
		<Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
			<Box sx={{ width: 10, height: 10, borderRadius: "50%", bgcolor: color }} />
			<Typography component="span" sx={{ fontWeight: 700 }}>
				{count}
			</Typography>
			<Typography component="span" color="text.secondary">
				{label}
			</Typography>
		</Stack>
	);
}

function SectionLabel({ children }: { children: ReactNode }) {
	return (
		<Typography
			variant="overline"
			color="text.secondary"
			sx={{ display: "block", letterSpacing: 1, mb: 1 }}
		>
			{children}
		</Typography>
	);
}

function HypervisorRow({ node }: { node: MonitoredHost }) {
	return (
		<Box
			sx={{
				display: "flex",
				alignItems: "center",
				gap: 2,
				px: 2,
				py: 1.5,
				borderRadius: 2,
				border: 1,
				borderColor: "divider",
				bgcolor: "action.hover",
			}}
		>
			<Chip
				size="small"
				color="secondary"
				variant="outlined"
				icon={<DnsOutlinedIcon />}
				label="Hyperviseur"
			/>
			<Mono>{node.hostname}</Mono>
			<Box sx={{ flexGrow: 1 }} />
			{node.ip ? <Mono>{node.ip}</Mono> : <Typography color="text.secondary">—</Typography>}
			<StatusChip host={node} />
		</Box>
	);
}

function groupHeader(group: VlanGroup): { badge: string | null; title: ReactNode; muted: boolean } {
	if (group.kind === "untagged") return { badge: null, title: "Non taggé", muted: true };
	if (group.kind === "no-address") return { badge: null, title: "Sans adresse", muted: true };

	return {
		badge: `VLAN ${group.vlan}`,
		title: group.subnet ? (
			<>
				<Mono>{group.subnet.cidr}</Mono>
				{group.subnet.label && (
					<Typography component="span" sx={{ ml: 1.5 }}>
						{group.subnet.label}
					</Typography>
				)}
			</>
		) : (
			<Typography component="span" color="text.secondary">
				Sous-réseau non configuré
			</Typography>
		),
		muted: false,
	};
}

function VlanGroupBlock({
	group,
	expanded,
	onToggle,
	onPatch,
}: {
	group: VlanGroup;
	expanded: boolean;
	onToggle: () => void;
	onPatch: (input: { key: string; pinned?: boolean; excluded?: boolean }) => void;
}) {
	const { badge, title, muted } = groupHeader(group);

	return (
		<Box sx={{ border: 1, borderColor: "divider", borderRadius: 2, overflow: "hidden" }}>
			<Box
				onClick={onToggle}
				sx={{
					display: "flex",
					alignItems: "center",
					gap: 1.5,
					px: 1.5,
					py: 1,
					cursor: "pointer",
					userSelect: "none",
					"&:hover": { bgcolor: "action.hover" },
				}}
			>
				<IconButton size="small" sx={{ p: 0.25 }}>
					{expanded ? (
						<ExpandMoreIcon fontSize="small" />
					) : (
						<ChevronRightIcon fontSize="small" />
					)}
				</IconButton>
				{badge && <Chip size="small" color="primary" variant="outlined" label={badge} />}
				<Box
					sx={{
						display: "flex",
						alignItems: "center",
						color: muted ? "text.secondary" : "text.primary",
					}}
				>
					{title}
				</Box>
				<Box sx={{ flexGrow: 1 }} />
				<Chip size="small" label={group.hosts.length} sx={{ bgcolor: "action.selected" }} />
			</Box>

			<Collapse in={expanded} unmountOnExit>
				<Box sx={{ borderTop: 1, borderColor: "divider" }}>
					{group.hosts.map((host) => (
						<HostRow key={host.key} host={host} onPatch={onPatch} />
					))}
				</Box>
			</Collapse>
		</Box>
	);
}

function HostRow({
	host,
	onPatch,
}: {
	host: MonitoredHost;
	onPatch: (input: { key: string; pinned?: boolean; excluded?: boolean }) => void;
}) {
	return (
		<Box
			sx={{
				display: "grid",
				gridTemplateColumns: ROW_COLUMNS,
				alignItems: "center",
				gap: 1.5,
				px: 1.5,
				py: 0.75,
				"&:not(:last-of-type)": { borderBottom: 1, borderColor: "divider" },
				"&:hover": { bgcolor: "action.hover" },
				"&:hover .host-actions": { opacity: 1 },
			}}
		>
			<HostTypeChip type={host.type} />

			<Box sx={{ minWidth: 0, display: "flex", alignItems: "center", gap: 1 }}>
				<Box sx={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
					<Mono>{host.hostname}</Mono>
				</Box>
				{host.pinned && <Chip size="small" variant="outlined" label="Épinglé" />}
				{host.excluded && (
					<Chip size="small" variant="outlined" color="warning" label="Exclu du DNS" />
				)}
			</Box>

			<Box sx={{ textAlign: "right" }}>
				{host.ip ? (
					<Mono>{host.ip}</Mono>
				) : (
					<Typography color="text.secondary">—</Typography>
				)}
			</Box>

			<Typography variant="body2" color="text.secondary" sx={{ textAlign: "right" }}>
				{host.vmId}
			</Typography>

			<Box>
				<StatusChip host={host} />
			</Box>

			<Box
				className="host-actions"
				sx={{
					display: "flex",
					justifyContent: "flex-end",
					opacity: 0,
					transition: "opacity 120ms",
				}}
			>
				<Tooltip
					title={host.pinned ? "Ne plus épingler" : "Épingler (survit à la rétention)"}
				>
					<IconButton
						size="small"
						onClick={() => onPatch({ key: host.key, pinned: !host.pinned })}
					>
						{host.pinned ? (
							<PushPinIcon fontSize="small" />
						) : (
							<PushPinOutlinedIcon fontSize="small" />
						)}
					</IconButton>
				</Tooltip>
				<Tooltip title={host.excluded ? "Réintégrer au DNS" : "Exclure du DNS"}>
					<IconButton
						size="small"
						onClick={() => onPatch({ key: host.key, excluded: !host.excluded })}
					>
						{host.excluded ? (
							<CheckCircleOutlineIcon fontSize="small" />
						) : (
							<BlockIcon fontSize="small" />
						)}
					</IconButton>
				</Tooltip>
			</Box>
		</Box>
	);
}

function StatusChip({ host }: { host: MonitoredHost }) {
	if (host.present) return <Chip size="small" color="success" label="Actif" />;
	// Retained past its last sighting: still resolvable until retention expires.
	return (
		<Tooltip title={`Vu pour la dernière fois : ${new Date(host.lastSeenAt).toLocaleString()}`}>
			<Chip size="small" color="warning" variant="outlined" label="Rétention" />
		</Tooltip>
	);
}
