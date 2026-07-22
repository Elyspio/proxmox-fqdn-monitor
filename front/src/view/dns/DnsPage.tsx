import {
	Alert,
	AlertTitle,
	Box,
	Button,
	Card,
	CardContent,
	Chip,
	Paper,
	Stack,
	Table,
	TableBody,
	TableCell,
	TableContainer,
	TableHead,
	TableRow,
	Typography,
} from "@mui/material";
import CloudUploadIcon from "@mui/icons-material/CloudUpload";
import { useSnackbar } from "notistack";
import { useDnsPushes, useDnsState, useSettings } from "@/core/api/queries";
import { usePushDns } from "@/core/api/mutations";
import type { ExistingRecord } from "@/core/api/types";
import { DateTimeText, Empty, Loading, Mono, PageTitle, QueryError, SpacedRow } from "@/view/components/Common";

function Metric({ label, value, color }: { label: string; value: number; color?: string }) {
	return (
		<Card sx={{ flex: 1 }}>
			<CardContent>
				<Typography variant="caption" color="text.secondary">
					{label}
				</Typography>
				<Typography variant="h5" sx={{ color }}>
					{value}
				</Typography>
			</CardContent>
		</Card>
	);
}

function RecordTable({ records, title, subtitle }: { records: ExistingRecord[]; title: ReactTitle; subtitle?: string }) {
	return (
		<TableContainer component={Paper}>
			<Stack sx={{ p: 2 }} spacing={0.5}>
				{title}
				{subtitle && (
					<Typography variant="body2" color="text.secondary">
						{subtitle}
					</Typography>
				)}
			</Stack>
			<Table size="small">
				<TableHead>
					<TableRow>
						<TableCell>Domaine</TableCell>
						<TableCell>Adresse</TableCell>
						<TableCell>TTL</TableCell>
					</TableRow>
				</TableHead>
				<TableBody>
					{records.map((record) => (
						<TableRow key={`${record.domain}-${record.ip}`}>
							<TableCell>
								<Mono>{record.domain}</Mono>
							</TableCell>
							<TableCell>
								<Mono>{record.ip}</Mono>
							</TableCell>
							<TableCell>{record.ttl}</TableCell>
						</TableRow>
					))}
				</TableBody>
			</Table>
		</TableContainer>
	);
}

type ReactTitle = React.ReactElement;

export function DnsPage() {
	const state = useDnsState();
	const pushes = useDnsPushes();
	const settings = useSettings();
	const push = usePushDns();
	const { enqueueSnackbar } = useSnackbar();

	if (state.isLoading || settings.isLoading) return <Loading />;
	if (state.isError) return <QueryError error={state.error} />;

	const reconciliationEnabled = settings.data?.reconciliationEnabled ?? false;
	const deleteOrphans = settings.data?.deleteOrphanRecords ?? false;

	const runPush = () =>
		push.mutate(undefined, {
			onSuccess: (results) => {
				const total = results.reduce((sum, result) => sum + result.written + result.deleted, 0);
				enqueueSnackbar(
					reconciliationEnabled
						? `${total} enregistrement(s) appliqué(s)`
						: `Simulation : ${total} changement(s) proposé(s)`,
					{ variant: reconciliationEnabled ? "success" : "info" },
				);
			},
			onError: (error) =>
				enqueueSnackbar(error instanceof Error ? error.message : "Échec du push", { variant: "error" }),
		});

	return (
		<Stack spacing={3}>
			<SpacedRow>
				<PageTitle>Export DNS</PageTitle>
				<Button startIcon={<CloudUploadIcon />} variant="contained" onClick={runPush} loading={push.isPending}>
					{reconciliationEnabled ? "Appliquer maintenant" : "Simuler maintenant"}
				</Button>
			</SpacedRow>

			{!reconciliationEnabled && (
				<Alert severity="info">
					<AlertTitle>Réconciliation désactivée</AlertTitle>
					La zone n&apos;est jamais modifiée. Le diff ci-dessous montre ce qui serait écrit. Activez la
					réconciliation dans les réglages une fois le diff vérifié.
				</Alert>
			)}

			{state.data?.map((zone) => (
				<Stack key={zone.zone} spacing={3}>
					{!zone.enabled && (
						<Alert severity="warning">
							Export Technitium désactivé ou incomplet. Renseignez URL, zone et jeton dans les réglages.
						</Alert>
					)}

					{zone.error && (
						<Alert severity="error">
							<AlertTitle>Zone {zone.zone} injoignable</AlertTitle>
							{zone.error}
						</Alert>
					)}

					{zone.enabled && !zone.error && (
						<>
							<Box sx={{ display: "flex", gap: 2 }}>
								<Metric label="À jour" value={zone.upToDate.length} />
								<Metric label="À écrire" value={zone.toWrite.length} color="warning.main" />
								<Metric label="Orphelins" value={zone.orphans.length} color="error.main" />
								<Metric label="Hors gestion" value={zone.unmanaged.length} />
							</Box>

							{zone.toWrite.length > 0 && (
								<TableContainer component={Paper}>
									<Typography variant="subtitle1" sx={{ p: 2 }}>
										Enregistrements à écrire dans <Mono>{zone.zone}</Mono>
									</Typography>
									<Table size="small">
										<TableHead>
											<TableRow>
												<TableCell>Domaine</TableCell>
												<TableCell>Adresse</TableCell>
											</TableRow>
										</TableHead>
										<TableBody>
											{zone.toWrite.map((record) => (
												<TableRow key={record.domain}>
													<TableCell>
														<Mono>{record.domain}</Mono>
													</TableCell>
													<TableCell>
														<Mono>{record.ip}</Mono>
													</TableCell>
												</TableRow>
											))}
										</TableBody>
									</Table>
								</TableContainer>
							)}

							{zone.orphans.length > 0 && (
								<RecordTable
									records={zone.orphans}
									title={
										<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
											<Typography variant="subtitle1">Orphelins</Typography>
											<Chip
												size="small"
												color={deleteOrphans ? "error" : "default"}
												label={deleteOrphans ? "Seront supprimés" : "Conservés"}
											/>
										</Box>
									}
								/>
							)}

							{zone.unmanaged.length > 0 && (
								<RecordTable
									records={zone.unmanaged}
									title={<Typography variant="subtitle1">Enregistrements hors gestion</Typography>}
									subtitle="Posés à la main dans la zone. Ils ne portent pas le marqueur de cet outil et ne sont donc jamais écrits ni supprimés."
								/>
							)}
						</>
					)}
				</Stack>
			))}

			<TableContainer component={Paper}>
				<Typography variant="subtitle1" sx={{ p: 2 }}>
					Derniers passages
				</Typography>
				<Table size="small">
					<TableHead>
						<TableRow>
							<TableCell>Date</TableCell>
							<TableCell>Mode</TableCell>
							<TableCell align="right">Écrits</TableCell>
							<TableCell align="right">Inchangés</TableCell>
							<TableCell align="right">Supprimés</TableCell>
							<TableCell align="right">Échecs</TableCell>
						</TableRow>
					</TableHead>
					<TableBody>
						{pushes.data?.map((entry) => (
							<TableRow key={entry.id}>
								<TableCell>
									<DateTimeText value={entry.at} />
								</TableCell>
								<TableCell>
									<Chip size="small" variant="outlined" label={entry.dryRun ? "Simulation" : "Appliqué"} />
								</TableCell>
								<TableCell align="right">{entry.written}</TableCell>
								<TableCell align="right">{entry.skipped}</TableCell>
								<TableCell align="right">{entry.deleted}</TableCell>
								<TableCell align="right">{entry.failed}</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
				{pushes.data?.length === 0 && <Empty>Aucun passage enregistré.</Empty>}
			</TableContainer>
		</Stack>
	);
}
