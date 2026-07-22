import {
	Alert,
	Box,
	Card,
	CardContent,
	Chip,
	Divider,
	Grid,
	List,
	ListItem,
	ListItemText,
	Stack,
	Typography,
} from "@mui/material";
import { useCollectionHealth } from "@/core/api/queries";
import {
	DateTimeText,
	Empty,
	HostTypeChip,
	Loading,
	Mono,
	OutcomeChip,
	PageTitle,
	QueryError,
	SpacedRow,
} from "@/view/components/Common";

/** .NET TimeSpan values arrive as [d.]hh:mm:ss[.fffffff]. */
function formatDuration(value: string): string {
	const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value);
	if (!match) return value;
	const [, days, hours, minutes, seconds, fraction] = match;
	const total =
		Number(days ?? 0) * 86400 +
		Number(hours) * 3600 +
		Number(minutes) * 60 +
		Number(seconds) +
		Number(`0.${fraction ?? 0}`);
	return total < 1 ? `${Math.round(total * 1000)} ms` : `${total.toFixed(1)} s`;
}

function Metric({ label, value }: { label: string; value: string | number }) {
	return (
		<Box>
			<Typography variant="caption" color="text.secondary">
				{label}
			</Typography>
			<Typography variant="h6">{value}</Typography>
		</Box>
	);
}

export function HealthPage() {
	const health = useCollectionHealth();

	if (health.isLoading) return <Loading />;
	if (health.isError) return <QueryError error={health.error} />;

	const runs = health.data ?? [];

	return (
		<Stack spacing={3}>
			<PageTitle>Santé de la collecte</PageTitle>

			{runs.length === 0 && (
				<Empty>Aucune collecte enregistrée. Déclarez un nœud dans les réglages puis lancez une collecte.</Empty>
			)}

			<Grid container spacing={3}>
				{runs.map((run) => (
					<Grid key={run.id} size={{ xs: 12, md: 6 }}>
						<Card>
							<CardContent>
								<SpacedRow sx={{ mb: 2 }}>
									<Typography variant="h6">{run.nodeName}</Typography>
									<OutcomeChip outcome={run.outcome} />
								</SpacedRow>

								<Box sx={{ display: "flex", gap: 3, mb: 2 }}>
									<Metric label="Découverts" value={run.hostsDiscovered} />
									<Metric label="Avec adresse" value={run.hostsWithIp} />
									<Metric label="Durée" value={formatDuration(run.duration)} />
								</Box>

								<Typography variant="body2" color="text.secondary">
									Dernière collecte : <DateTimeText value={run.startedAt} />
								</Typography>

								{run.errors.length > 0 && (
									<Alert severity="error" sx={{ mt: 2 }}>
										{run.errors.map((error) => (
											<Typography key={error} variant="body2">
												{error}
											</Typography>
										))}
									</Alert>
								)}

								{run.issues.length > 0 && (
									<>
										<Divider sx={{ my: 2 }} />
										<Typography variant="subtitle2" gutterBottom>
											Hôtes sans adresse exploitable
											<Chip size="small" label={run.issues.length} sx={{ ml: 1 }} />
										</Typography>
										<List dense disablePadding>
											{run.issues.map((issue) => (
												<ListItem key={`${issue.type}-${issue.vmId}`} disableGutters>
													<ListItemText
														primary={
															<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
																<HostTypeChip type={issue.type} />
																<Mono>{issue.hostname}</Mono>
																<Typography variant="caption" color="text.secondary">
																	#{issue.vmId}
																</Typography>
															</Box>
														}
														secondary={issue.reason}
													/>
												</ListItem>
											))}
										</List>
									</>
								)}
							</CardContent>
						</Card>
					</Grid>
				))}
			</Grid>
		</Stack>
	);
}
