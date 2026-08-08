import { type ReactNode, useMemo } from "react";
import {
	Box,
	Button,
	Chip,
	IconButton,
	Stack,
	type SxProps,
	type Theme,
	TextField,
	Tooltip,
	Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import AutoFixHighIcon from "@mui/icons-material/AutoFixHigh";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutlined";
import WarningAmberIcon from "@mui/icons-material/WarningAmber";
import { useHosts } from "@/core/api/queries";
import type { MonitoredHost, Subnet } from "@/core/api/types";
import { cidrPrefix, ipInCidr, suggestCidr } from "@/view/hosts/vlan";

// vlan badge | cidr | label | remove — one grid so every row lines up across both sections.
const GRID = "104px minmax(0, 1.3fr) minmax(0, 1.3fr) 48px";

/** A discovered VLAN and the addresses seen behind its tag. `vlan === null` is the untagged bucket. */
interface VlanGroup {
	vlan: number | null;
	ips: string[];
}

function vlanKey(vlan: number | null): string {
	return vlan === null ? "untagged" : `vlan:${vlan}`;
}

/**
 * VLAN-first editor for the configured subnets. VLAN rows are discovered from the collected hosts
 * (`useHosts`); each is joined by longest-prefix IP match to a subnet in the editable `Subnet[]`
 * draft, which is the persisted model — this component only reshapes how that flat list is presented
 * and edited. The subnet's CIDR is still the collection filter (the union of every CIDR is the
 * allow-list); the VLAN number itself comes from the guest's real NIC tag, never from configuration.
 */
export function SubnetsEditor({
	value,
	onChange,
}: {
	value: Subnet[];
	onChange: (next: Subnet[]) => void;
}) {
	const hosts = useHosts();

	// Only guests carry a VLAN tag, and a host needs an address to inform (and be filtered by) a CIDR.
	const guests = useMemo(
		() => (hosts.data ?? []).filter((host) => host.type !== "Node" && host.ip),
		[hosts.data],
	);

	// Bucket guests by their real NIC tag. Derived purely from host data, so a row never jumps while a
	// CIDR is being typed. Numbered VLANs sort ascending; the untagged bucket sorts last.
	const groups = useMemo<VlanGroup[]>(() => {
		const byKey = new Map<string, VlanGroup>();
		for (const guest of guests) {
			const key = vlanKey(guest.vlan);
			const group = byKey.get(key) ?? { vlan: guest.vlan, ips: [] };
			if (guest.ip) group.ips.push(guest.ip);
			byKey.set(key, group);
		}
		return [...byKey.values()].sort((a, b) => {
			if (a.vlan === null) return 1;
			if (b.vlan === null) return -1;
			return a.vlan - b.vlan;
		});
	}, [guests]);

	// Which subnet each VLAN row edits, by longest-prefix IP match. Keyed on value.length, not its
	// contents, on purpose: editing a CIDR (same array length) must not re-shuffle which subnet a row
	// is bound to — otherwise a half-typed CIDR would detach the row mid-keystroke. Adding or removing
	// a subnet, or a newly discovered VLAN, is what recomputes the binding.
	const association = useMemo(() => {
		const assoc = new Map<string, number>();
		for (const group of groups) {
			let bestIndex = -1;
			let bestPrefix = -1;
			value.forEach((subnet, index) => {
				const prefix = cidrPrefix(subnet.cidr);
				if (prefix === null || prefix <= bestPrefix) return;
				if (group.ips.some((ip) => ipInCidr(subnet.cidr, ip))) {
					bestIndex = index;
					bestPrefix = prefix;
				}
			});
			if (bestIndex >= 0) assoc.set(vlanKey(group.vlan), bestIndex);
		}
		return assoc;
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [groups, value.length]);

	// Subnets no VLAN row claims: freshly added bootstrap rows and genuine orphans. Never auto-pruned.
	const referenced = useMemo(() => new Set(association.values()), [association]);
	const residual = value
		.map((subnet, index) => ({ subnet, index }))
		.filter(({ index }) => !referenced.has(index));

	const update = (index: number, changes: Partial<Subnet>) =>
		onChange(value.map((subnet, i) => (i === index ? { ...subnet, ...changes } : subnet)));
	const remove = (index: number) => onChange(value.filter((_, i) => i !== index));
	const add = (subnet: Subnet = { cidr: "", label: null }) => onChange([...value, subnet]);

	const empty = groups.length === 0 && residual.length === 0;

	return (
		<Box>
			<Typography variant="subtitle2" sx={{ mb: 0.5 }}>
				Sous-réseaux par VLAN
			</Typography>
			<Typography variant="caption" color="text.secondary" sx={{ display: "block", mb: 1.5 }}>
				Les VLAN sont découverts depuis les hôtes collectés. Le CIDR d&apos;un VLAN filtre
				les adresses conservées ; il peut être resserré automatiquement sur les hôtes vus.
			</Typography>

			{groups.length > 0 && (
				<>
					<ColumnHeader />
					<Stack spacing={1}>
						{groups.map((group) => {
							const index = association.get(vlanKey(group.vlan));
							return (
								<VlanRow
									key={vlanKey(group.vlan)}
									group={group}
									index={index}
									subnet={index === undefined ? undefined : value[index]}
									onUpdate={update}
									onRemove={remove}
									onCreate={add}
								/>
							);
						})}
					</Stack>
				</>
			)}

			{residual.length > 0 && (
				<Box sx={{ mt: groups.length > 0 ? 2.5 : 0 }}>
					<SectionLabel>Non associés à un VLAN</SectionLabel>
					<Stack spacing={1}>
						{residual.map(({ subnet, index }) => (
							<ResidualRow
								// Index key: rows carry no stable id; a remove recomputes the whole list.
								key={index}
								subnet={subnet}
								hostCount={countHosts(guests, subnet.cidr)}
								onUpdate={(changes) => update(index, changes)}
								onRemove={() => remove(index)}
							/>
						))}
					</Stack>
				</Box>
			)}

			{empty && (
				<Typography variant="body2" color="text.secondary">
					Aucun sous-réseau. Ajoutez-en un pour démarrer la collecte ; les VLAN
					apparaîtront ici une fois leurs hôtes vus.
				</Typography>
			)}

			<Button startIcon={<AddIcon />} onClick={() => add()} size="small" sx={{ mt: 1.5 }}>
				Ajouter un sous-réseau
			</Button>
		</Box>
	);
}

function countHosts(guests: readonly MonitoredHost[], cidr: string): number {
	return guests.filter((guest) => guest.ip !== null && ipInCidr(cidr, guest.ip)).length;
}

function VlanRow({
	group,
	index,
	subnet,
	onUpdate,
	onRemove,
	onCreate,
}: {
	group: VlanGroup;
	index?: number;
	subnet?: Subnet;
	onUpdate: (index: number, changes: Partial<Subnet>) => void;
	onRemove: (index: number) => void;
	onCreate: (subnet: Subnet) => void;
}) {
	const suggestion = suggestCidr(group.ips);
	const badge = group.vlan === null ? "Non taggé" : `VLAN ${group.vlan}`;

	// A discovered VLAN with no configured subnet: only reachable after removing its subnet by hand.
	// Offer to (re)create one, prefilled with the tight suggestion so it collects exactly these hosts.
	if (index === undefined || subnet === undefined) {
		return (
			<Box sx={{ display: "grid", gridTemplateColumns: GRID, gap: 1, alignItems: "center" }}>
				<VlanBadge label={badge} />
				<Box sx={{ gridColumn: "2 / -1" }}>
					<Button
						size="small"
						startIcon={<AddIcon />}
						onClick={() => onCreate({ cidr: suggestion ?? "", label: null })}
					>
						Définir un sous-réseau{suggestion ? ` (${suggestion})` : ""}
					</Button>
				</Box>
			</Box>
		);
	}

	const currentPrefix = cidrPrefix(subnet.cidr);
	const suggestedPrefix = suggestion === null ? null : cidrPrefix(suggestion);
	// Only offer the suggestion when it is strictly narrower than the current CIDR — then it reads as
	// "you can tighten this", never as noise on a range that already fits.
	const canTighten =
		suggestion !== null &&
		suggestion !== subnet.cidr &&
		suggestedPrefix !== null &&
		(currentPrefix === null || suggestedPrefix > currentPrefix);
	const detached = group.ips.length > 0 && !group.ips.some((ip) => ipInCidr(subnet.cidr, ip));

	return (
		<Box sx={{ display: "grid", gridTemplateColumns: GRID, gap: 1, alignItems: "start" }}>
			<VlanBadge label={badge} sx={{ mt: 0.75 }} />
			<Box>
				<TextField
					size="small"
					fullWidth
					placeholder="10.0.0.0/24"
					value={subnet.cidr}
					error={detached}
					onChange={(event) => onUpdate(index, { cidr: event.target.value })}
				/>
				{canTighten && (
					<Chip
						size="small"
						variant="outlined"
						color="primary"
						icon={<AutoFixHighIcon />}
						label={`Resserrer en ${suggestion}`}
						onClick={() => onUpdate(index, { cidr: suggestion })}
						sx={{ mt: 0.5 }}
					/>
				)}
				{detached && (
					<Typography
						variant="caption"
						color="error"
						sx={{ display: "flex", alignItems: "center", gap: 0.5, mt: 0.5 }}
					>
						<WarningAmberIcon sx={{ fontSize: 14 }} />
						Ce CIDR ne contient aucun hôte de ce VLAN.
					</Typography>
				)}
			</Box>
			<TextField
				size="small"
				fullWidth
				placeholder="Libellé (facultatif)"
				value={subnet.label ?? ""}
				onChange={(event) => onUpdate(index, { label: event.target.value || null })}
			/>
			<Tooltip title="Retirer ce sous-réseau">
				<IconButton size="small" onClick={() => onRemove(index)} sx={{ mt: 0.25 }}>
					<DeleteOutlineIcon fontSize="small" />
				</IconButton>
			</Tooltip>
		</Box>
	);
}

function ResidualRow({
	subnet,
	hostCount,
	onUpdate,
	onRemove,
}: {
	subnet: Subnet;
	hostCount: number;
	onUpdate: (changes: Partial<Subnet>) => void;
	onRemove: () => void;
}) {
	return (
		<Box sx={{ display: "grid", gridTemplateColumns: GRID, gap: 1, alignItems: "start" }}>
			<Chip
				size="small"
				variant="outlined"
				color={hostCount === 0 ? "default" : "warning"}
				label={
					hostCount === 0 ? "Aucun hôte" : `${hostCount} hôte${hostCount > 1 ? "s" : ""}`
				}
				sx={{ mt: 0.25 }}
			/>
			<TextField
				size="small"
				fullWidth
				placeholder="10.0.0.0/24"
				value={subnet.cidr}
				onChange={(event) => onUpdate({ cidr: event.target.value })}
			/>
			<TextField
				size="small"
				fullWidth
				placeholder="Libellé (facultatif)"
				value={subnet.label ?? ""}
				onChange={(event) => onUpdate({ label: event.target.value || null })}
			/>
			<Tooltip title="Retirer ce sous-réseau">
				<IconButton size="small" onClick={onRemove} sx={{ mt: 0.25 }}>
					<DeleteOutlineIcon fontSize="small" />
				</IconButton>
			</Tooltip>
		</Box>
	);
}

function VlanBadge({ label, sx }: { label: string; sx?: SxProps<Theme> }) {
	return (
		<Box
			component="span"
			sx={{
				display: "inline-flex",
				alignItems: "center",
				justifyContent: "center",
				px: 1,
				py: 0.25,
				borderRadius: 1,
				fontSize: 12,
				fontWeight: 600,
				whiteSpace: "nowrap",
				bgcolor: "action.selected",
				color: "text.secondary",
				...sx,
			}}
		>
			{label}
		</Box>
	);
}

function ColumnHeader() {
	return (
		<Box
			sx={{
				display: "grid",
				gridTemplateColumns: GRID,
				gap: 1,
				px: 0.25,
				mb: 0.5,
				color: "text.secondary",
			}}
		>
			<Typography variant="caption">VLAN</Typography>
			<Typography variant="caption">CIDR</Typography>
			<Typography variant="caption">Libellé</Typography>
			<span />
		</Box>
	);
}

function SectionLabel({ children }: { children: ReactNode }) {
	return (
		<Typography
			variant="overline"
			color="text.secondary"
			sx={{ display: "block", letterSpacing: 1, fontWeight: 600, mb: 1 }}
		>
			{children}
		</Typography>
	);
}
