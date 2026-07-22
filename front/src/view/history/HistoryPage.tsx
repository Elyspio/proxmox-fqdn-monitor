import { useState } from "react";
import {
	Box,
	Button,
	MenuItem,
	Paper,
	Stack,
	Table,
	TableBody,
	TableCell,
	TableContainer,
	TableHead,
	TableRow,
	TextField,
	Typography,
} from "@mui/material";
import { useEvents } from "@/core/api/queries";
import type { IpEventKind } from "@/core/api/types";
import {
	DateTimeText,
	Empty,
	EventChip,
	HostTypeChip,
	Loading,
	Mono,
	PageTitle,
	QueryError,
	SpacedRow,
} from "@/view/components/Common";

const PAGE_SIZE = 100;

const kinds: (IpEventKind | "")[] = ["", "Appeared", "Changed", "Disappeared", "Renamed"];

const kindLabels: Record<string, string> = {
	"": "Tous",
	Appeared: "Apparu",
	Changed: "Changé",
	Disappeared: "Disparu",
	Renamed: "Renommé",
};

export function HistoryPage() {
	const [kind, setKind] = useState<IpEventKind | "">("");
	const [page, setPage] = useState(0);

	const events = useEvents(undefined, kind || undefined, page * PAGE_SIZE, PAGE_SIZE);

	if (events.isLoading) return <Loading />;
	if (events.isError) return <QueryError error={events.error} />;

	const data = events.data;
	const hasNext = data ? (page + 1) * PAGE_SIZE < data.total : false;

	return (
		<Stack spacing={3}>
			<SpacedRow>
				<PageTitle>Historique</PageTitle>
				<TextField
					select
					size="small"
					label="Type"
					value={kind}
					onChange={(event) => {
						setKind(event.target.value as IpEventKind | "");
						setPage(0);
					}}
					sx={{ width: 200 }}
				>
					{kinds.map((value) => (
						<MenuItem key={value || "all"} value={value}>
							{kindLabels[value]}
						</MenuItem>
					))}
				</TextField>
			</SpacedRow>

			<TableContainer component={Paper}>
				<Table size="small">
					<TableHead>
						<TableRow>
							<TableCell>Date</TableCell>
							<TableCell>Hôte</TableCell>
							<TableCell>Type</TableCell>
							<TableCell>Nœud</TableCell>
							<TableCell>Événement</TableCell>
							<TableCell>Avant</TableCell>
							<TableCell>Après</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{data?.items.map((event) => (
							<TableRow key={event.id} hover>
								<TableCell>
									<DateTimeText value={event.at} />
								</TableCell>
								<TableCell>
									<Mono>{event.hostname}</Mono>
								</TableCell>
								<TableCell>
									<HostTypeChip type={event.type} />
								</TableCell>
								<TableCell>{event.nodeName}</TableCell>
								<TableCell>
									<EventChip kind={event.kind} />
								</TableCell>
								<TableCell>{event.previousIp ? <Mono>{event.previousIp}</Mono> : "—"}</TableCell>
								<TableCell>{event.currentIp ? <Mono>{event.currentIp}</Mono> : "—"}</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
				{data?.items.length === 0 && <Empty>Aucun changement enregistré.</Empty>}
			</TableContainer>

			<Box sx={{ display: "flex", justifyContent: "flex-end", alignItems: "center", gap: 2 }}>
				<Typography variant="body2" color="text.secondary">
					{data?.total ?? 0} événement(s)
				</Typography>
				<Button disabled={page === 0} onClick={() => setPage((value) => value - 1)}>
					Précédent
				</Button>
				<Button disabled={!hasNext} onClick={() => setPage((value) => value + 1)}>
					Suivant
				</Button>
			</Box>
		</Stack>
	);
}
