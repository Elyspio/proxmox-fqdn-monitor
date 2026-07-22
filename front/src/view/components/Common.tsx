import { Alert, Box, Chip, CircularProgress, Typography } from "@mui/material";
import type { ReactNode } from "react";
import { monospace } from "@/config/theme";
import type { CollectionOutcome, HostType, IpEventKind } from "@/core/api/types";

/** Anything that is an address, a hostname or a zone name renders through this. */
export function Mono({ children }: { children: ReactNode }) {
	return <Box component="span" sx={{ fontFamily: monospace, fontSize: "0.875rem" }}>{children}</Box>;
}

export function Loading() {
	return (
		<Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
			<CircularProgress />
		</Box>
	);
}

export function QueryError({ error }: { error: unknown }) {
	const message = error instanceof Error ? error.message : String(error);
	return <Alert severity="error">{message}</Alert>;
}

export function Empty({ children }: { children: ReactNode }) {
	return (
		<Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
			{children}
		</Typography>
	);
}

const hostTypeLabels: Record<HostType, string> = {
	Node: "Hyperviseur",
	Vm: "VM",
	Container: "LXC",
};

export function HostTypeChip({ type }: { type: HostType }) {
	const color = type === "Node" ? "secondary" : type === "Vm" ? "primary" : "default";
	return <Chip size="small" label={hostTypeLabels[type]} color={color} variant="outlined" />;
}

export function OutcomeChip({ outcome }: { outcome: CollectionOutcome }) {
	const color = outcome === "Succeeded" ? "success" : outcome === "Partial" ? "warning" : "error";
	const label = outcome === "Succeeded" ? "OK" : outcome === "Partial" ? "Partiel" : "Échec";
	return <Chip size="small" label={label} color={color} />;
}

const eventLabels: Record<IpEventKind, string> = {
	Appeared: "Apparu",
	Changed: "Changé",
	Disappeared: "Disparu",
	Renamed: "Renommé",
};

export function EventChip({ kind }: { kind: IpEventKind }) {
	const color =
		kind === "Appeared" ? "success" : kind === "Changed" ? "warning" : kind === "Disappeared" ? "error" : "default";
	return <Chip size="small" label={eventLabels[kind]} color={color} variant="outlined" />;
}

/** Absolute local time — a monitoring screen needs the actual instant, not "3 hours ago". */
export function DateTimeText({ value }: { value: string }) {
	return <Mono>{new Date(value).toLocaleString()}</Mono>;
}

/** Section heading used at the top of every page. */
export function PageTitle({ children }: { children: ReactNode }) {
	return (
		<Typography variant="h5" sx={{ fontWeight: 700 }}>
			{children}
		</Typography>
	);
}

/** Horizontal row that pushes its last child to the right edge. */
export function SpacedRow({ children, sx }: { children: ReactNode; sx?: object }) {
	return (
		<Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 2, ...sx }}>
			{children}
		</Box>
	);
}
