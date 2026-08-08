import { type ReactNode, useCallback, useMemo, useState } from "react";
import {
	alpha,
	Box,
	Chip,
	Collapse,
	IconButton,
	Stack,
	TextField,
	type Theme,
	Tooltip,
	Typography,
	useTheme,
} from "@mui/material";
import PushPinIcon from "@mui/icons-material/PushPin";
import PushPinOutlinedIcon from "@mui/icons-material/PushPinOutlined";
import BlockIcon from "@mui/icons-material/Block";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutlineOutlined";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import { monospace, type PaletteMode } from "@/config/theme";
import { useHosts, useSettings } from "@/core/api/queries";
import { usePatchHost } from "@/core/api/mutations";
import type { HostType, MonitoredHost } from "@/core/api/types";
import { Empty, Loading, Mono, QueryError } from "@/view/components/Common";
import { buildVlanGroups, type VlanGroup } from "./vlan";

const COLLAPSE_KEY = "hosts.collapsedVlans";

// type | name | ip | vmid | status | actions — one grid so columns line up across every group.
const ROW_COLUMNS = "52px minmax(0, 1fr) 120px 46px 100px 64px";

// Width of the tree gutter that holds the connector trunk and the branch ticks. Header badges and
// host type badges both start at this offset so a group's contents line up under its title.
const RAIL = 58;
const TRUNK_X = 13;

// One accent per VLAN, cycled in group order. Tuned per palette so a hue stays legible on either
// background: the trunk, the tick and the VLAN badge of a group all share its colour.
const ACCENTS: Record<PaletteMode, readonly string[]> = {
	light: ["#2e7de9", "#0f8a6e", "#8a3ffc", "#b45309", "#d1477a", "#0e7490", "#4d7c0f", "#c2410c"],
	dark: ["#7aa2f7", "#73daca", "#bb9af7", "#e0af68", "#f7768e", "#2ac3de", "#9ece6a", "#ff9e64"],
};

const CARD_RADIUS = "16px";

// Lifts a block off the page. Kept soft: a monitoring screen should read as one calm surface, not a
// stack of heavy panels. Dark mode leans on the paper/border contrast instead of a visible drop.
const cardShadow = (t: Theme) =>
	t.palette.mode === "light"
		? "0 1px 2px rgba(16, 20, 40, 0.05), 0 6px 16px rgba(16, 20, 40, 0.05)"
		: "0 1px 2px rgba(0, 0, 0, 0.45)";

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
	const theme = useTheme();
	const hosts = useHosts();
	const settings = useSettings();
	const patch = usePatchHost();
	const [filter, setFilter] = useState("");
	const { collapsed, toggle } = usePersistentCollapse(COLLAPSE_KEY);

	const accents = ACCENTS[theme.palette.mode as PaletteMode];
	const mutedAccent = theme.palette.text.secondary;

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

	// Bucket every guest by its real VLAN tag once, before filtering, so a group keeps its colour no
	// matter what the search box hides.
	const allGroups = useMemo(() => buildVlanGroups(guests, subnets), [guests, subnets]);

	const accentByVlan = useMemo(() => {
		const map = new Map<number, string>();
		let index = 0;
		for (const group of allGroups) {
			if (group.kind === "vlan" && group.vlan !== null) {
				map.set(group.vlan, accents[index % accents.length]);
				index += 1;
			}
		}
		return map;
	}, [allGroups, accents]);

	const groups = useMemo(
		() =>
			allGroups
				.map((group) => ({ ...group, hosts: group.hosts.filter(matches) }))
				.filter((group) => group.hosts.length > 0),
		[allGroups, matches],
	);

	if (hosts.isLoading || settings.isLoading) return <Loading />;
	if (hosts.isError) return <QueryError error={hosts.error} />;
	if (settings.isError) return <QueryError error={settings.error} />;

	const nothing = nodes.length === 0 && groups.length === 0;

	const accentFor = (group: VlanGroup) =>
		group.kind === "vlan" && group.vlan !== null
			? (accentByVlan.get(group.vlan) ?? mutedAccent)
			: mutedAccent;

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
							<Stack spacing={1.5}>
								{groups.map((group) => (
									<VlanGroupBlock
										key={group.id}
										group={group}
										accent={accentFor(group)}
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
			sx={{ display: "block", letterSpacing: 1.5, fontWeight: 600, mb: 1.5, ml: "2px" }}
		>
			{children}
		</Typography>
	);
}

/** Rounded colour-tinted label used for the VLAN tag and the "Hyperviseur" marker. */
function Pill({ color, bold, children }: { color: string; bold?: boolean; children: ReactNode }) {
	return (
		<Box
			component="span"
			sx={{
				display: "inline-flex",
				alignItems: "center",
				px: 1.25,
				py: 0.25,
				borderRadius: 999,
				fontSize: 12,
				fontWeight: bold ? 700 : 600,
				letterSpacing: "0.3px",
				whiteSpace: "nowrap",
				color,
				bgcolor: alpha(color, 0.13),
				border: 1,
				borderColor: alpha(color, 0.32),
			}}
		>
			{children}
		</Box>
	);
}

/** Monospaced VM / LXC marker; VM is tinted with the primary hue, LXC stays neutral. */
function TypeBadge({ type }: { type: HostType }) {
	const isVm = type === "Vm";
	const label = isVm ? "VM" : type === "Container" ? "LXC" : "HV";
	return (
		<Box
			component="span"
			sx={{
				justifySelf: "start",
				display: "inline-flex",
				alignItems: "center",
				justifyContent: "center",
				minWidth: 46,
				px: 0.75,
				py: "3px",
				borderRadius: 1.25,
				fontFamily: monospace,
				fontSize: 11,
				fontWeight: 700,
				letterSpacing: "0.5px",
				...(isVm
					? {
							color: "primary.main",
							bgcolor: (t) => alpha(t.palette.primary.main, 0.1),
							border: 1,
							borderColor: (t) => alpha(t.palette.primary.main, 0.24),
						}
					: {
							color: "text.secondary",
							bgcolor: "action.hover",
							border: 1,
							borderColor: "divider",
						}),
			}}
		>
			{label}
		</Box>
	);
}

/** Small circle carrying a group's host count, sitting at the right edge of the header. */
function CountBadge({ count }: { count: number }) {
	return (
		<Box
			component="span"
			sx={{
				flex: "0 0 auto",
				minWidth: 24,
				height: 24,
				px: 0.75,
				borderRadius: 999,
				bgcolor: "action.selected",
				color: "text.secondary",
				fontSize: 12,
				fontWeight: 600,
				display: "inline-flex",
				alignItems: "center",
				justifyContent: "center",
			}}
		>
			{count}
		</Box>
	);
}

/**
 * The tree gutter to the left of a row. Draws a vertical trunk plus a horizontal branch tick, tinted
 * with the group's accent. `header` originates the trunk at the row centre; `isLast` terminates it
 * there, so a group renders as ├─ rows capped by a └─.
 */
function Rail({
	accent,
	header,
	expanded,
	isLast,
	children,
}: {
	accent: string;
	header?: boolean;
	expanded?: boolean;
	isLast?: boolean;
	children?: ReactNode;
}) {
	const line = alpha(accent, 0.5);
	const trunk = header
		? expanded
			? { top: "calc(50% - 7px)", bottom: 0 }
			: { top: "calc(50% - 7px)", height: 14 }
		: isLast
			? { top: 0, height: "50%" }
			: { top: 0, bottom: 0 };

	return (
		<Box sx={{ position: "relative", flex: `0 0 ${RAIL}px` }}>
			<Box
				sx={{
					position: "absolute",
					left: `${TRUNK_X}px`,
					width: "2px",
					bgcolor: line,
					...trunk,
				}}
			/>
			<Box
				sx={{
					position: "absolute",
					top: "50%",
					mt: "-1px",
					height: "2px",
					left: `${TRUNK_X}px`,
					width: header ? 11 : 41,
					bgcolor: line,
				}}
			/>
			{children}
		</Box>
	);
}

function HypervisorRow({ node }: { node: MonitoredHost }) {
	const theme = useTheme();
	return (
		<Box
			sx={{
				display: "flex",
				alignItems: "center",
				gap: 2,
				px: 2.25,
				py: 1.5,
				borderRadius: CARD_RADIUS,
				border: 1,
				borderColor: alpha(theme.palette.secondary.main, 0.28),
				bgcolor: alpha(theme.palette.secondary.main, 0.06),
				boxShadow: cardShadow,
			}}
		>
			<Pill color={theme.palette.secondary.main}>Hyperviseur</Pill>
			<Mono>{node.hostname}</Mono>
			<Box sx={{ flexGrow: 1 }} />
			{node.ip ? (
				<Box sx={{ color: "text.secondary" }}>
					<Mono>{node.ip}</Mono>
				</Box>
			) : (
				<Typography color="text.secondary">—</Typography>
			)}
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
				<Box sx={{ color: "text.secondary" }}>
					<Mono>{group.subnet.cidr}</Mono>
				</Box>
				{group.subnet.label && (
					<Typography component="span" sx={{ fontSize: 14 }}>
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
	accent,
	expanded,
	onToggle,
	onPatch,
}: {
	group: VlanGroup;
	accent: string;
	expanded: boolean;
	onToggle: () => void;
	onPatch: (input: { key: string; pinned?: boolean; excluded?: boolean }) => void;
}) {
	const { badge, title, muted } = groupHeader(group);

	return (
		<Box
			sx={{
				bgcolor: "background.paper",
				border: 1,
				borderColor: alpha(accent, 0.28),
				borderRadius: CARD_RADIUS,
				overflow: "hidden",
				boxShadow: cardShadow,
			}}
		>
			<Box
				onClick={onToggle}
				sx={{
					display: "flex",
					alignItems: "stretch",
					cursor: "pointer",
					userSelect: "none",
					bgcolor: alpha(accent, 0.05),
					borderBottom: expanded ? 1 : 0,
					borderColor: "divider",
					transition: "background-color 120ms",
					"&:hover": { bgcolor: alpha(accent, 0.1) },
				}}
			>
				<Rail accent={accent} header expanded={expanded}>
					<IconButton
						size="small"
						sx={{
							position: "absolute",
							left: "27px",
							top: "50%",
							transform: "translateY(-50%)",
							p: 0.25,
							color: "text.secondary",
						}}
					>
						<ExpandMoreIcon
							fontSize="small"
							sx={{
								transition: "transform 150ms",
								transform: expanded ? "none" : "rotate(-90deg)",
							}}
						/>
					</IconButton>
				</Rail>
				<Box
					sx={{
						flex: 1,
						display: "flex",
						alignItems: "center",
						gap: 1.5,
						py: 1,
						pr: 1,
						minWidth: 0,
					}}
				>
					{badge && (
						<Pill color={accent} bold>
							{badge}
						</Pill>
					)}
					<Box
						sx={{
							display: "flex",
							alignItems: "baseline",
							gap: 1.5,
							minWidth: 0,
							color: muted ? "text.secondary" : "text.primary",
						}}
					>
						{title}
					</Box>
					<Box sx={{ flexGrow: 1 }} />
					<CountBadge count={group.hosts.length} />
				</Box>
			</Box>

			<Collapse in={expanded} unmountOnExit>
				{group.hosts.map((host, index) => (
					<HostRow
						key={host.key}
						host={host}
						accent={accent}
						isLast={index === group.hosts.length - 1}
						onPatch={onPatch}
					/>
				))}
			</Collapse>
		</Box>
	);
}

function HostRow({
	host,
	accent,
	isLast,
	onPatch,
}: {
	host: MonitoredHost;
	accent: string;
	isLast: boolean;
	onPatch: (input: { key: string; pinned?: boolean; excluded?: boolean }) => void;
}) {
	return (
		<Box
			sx={{
				display: "flex",
				alignItems: "stretch",
				"&:hover": { bgcolor: alpha(accent, 0.06) },
				"&:hover .host-actions": { opacity: 1 },
			}}
		>
			<Rail accent={accent} isLast={isLast} />

			<Box
				sx={{
					flex: 1,
					display: "grid",
					gridTemplateColumns: ROW_COLUMNS,
					alignItems: "center",
					gap: 1.5,
					py: 0.85,
					pr: 1,
				}}
			>
				<TypeBadge type={host.type} />

				<Box sx={{ minWidth: 0, display: "flex", alignItems: "center", gap: 1 }}>
					<Box
						sx={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}
					>
						<Mono>{host.hostname}</Mono>
					</Box>
					{host.pinned && <Chip size="small" variant="outlined" label="Épinglé" />}
					{host.excluded && (
						<Chip
							size="small"
							variant="outlined"
							color="warning"
							label="Exclu du DNS"
						/>
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
						title={
							host.pinned ? "Ne plus épingler" : "Épingler (survit à la rétention)"
						}
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
		</Box>
	);
}

function StatusChip({ host }: { host: MonitoredHost }) {
	const theme = useTheme();
	const warn = theme.palette.warning.main;
	// The theme's success hue is muddy; a dedicated green reads as "healthy" on either background.
	const green = theme.palette.mode === "dark" ? "#9ece6a" : "#2f9e44";

	if (host.present) {
		return (
			<Box
				component="span"
				sx={{
					display: "inline-flex",
					alignItems: "center",
					px: 1.25,
					py: 0.25,
					borderRadius: 999,
					fontSize: 12,
					fontWeight: 600,
					color: green,
					bgcolor: alpha(green, theme.palette.mode === "dark" ? 0.18 : 0.16),
				}}
			>
				Actif
			</Box>
		);
	}
	// Retained past its last sighting: still resolvable until retention expires.
	return (
		<Tooltip title={`Vu pour la dernière fois : ${new Date(host.lastSeenAt).toLocaleString()}`}>
			<Box
				component="span"
				sx={{
					display: "inline-flex",
					alignItems: "center",
					px: 1.25,
					py: 0.25,
					borderRadius: 999,
					fontSize: 12,
					fontWeight: 600,
					color: warn,
					border: 1,
					borderColor: alpha(warn, 0.45),
				}}
			>
				Rétention
			</Box>
		</Tooltip>
	);
}
