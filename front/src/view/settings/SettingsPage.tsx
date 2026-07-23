import { useEffect, useState } from "react";
import {
	Alert,
	AlertTitle,
	Box,
	Button,
	Card,
	CardContent,
	Checkbox,
	FormControlLabel,
	Stack,
	TextField,
	Typography,
} from "@mui/material";
import { useSnackbar } from "notistack";
import { useSettings } from "@/core/api/queries";
import { useSaveSettings } from "@/core/api/mutations";
import type { SettingsWriteDto } from "@/core/api/types";
import { Loading, PageTitle, QueryError } from "@/view/components/Common";
import { NodesCard } from "./NodesCard";
import { SubnetsEditor } from "./SubnetsEditor";

/** The API speaks .NET TimeSpan; the form speaks seconds. */
function toSeconds(timespan: string): number {
	const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/.exec(timespan);
	if (!match) return 60;
	const [, days, hours, minutes, seconds] = match;
	return (
		Number(days ?? 0) * 86400 + Number(hours) * 3600 + Number(minutes) * 60 + Number(seconds)
	);
}

function toTimespan(seconds: number): string {
	const clamped = Math.max(15, Math.floor(seconds));
	const h = Math.floor(clamped / 3600);
	const m = Math.floor((clamped % 3600) / 60);
	const s = clamped % 60;
	return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

export function SettingsPage() {
	const settings = useSettings();
	const save = useSaveSettings();
	const { enqueueSnackbar } = useSnackbar();

	const [form, setForm] = useState<SettingsWriteDto | null>(null);
	const [pollSeconds, setPollSeconds] = useState(60);
	const [apiToken, setApiToken] = useState("");

	useEffect(() => {
		if (!settings.data) return;
		const data = settings.data;
		setPollSeconds(toSeconds(data.pollInterval));
		setForm({
			pollInterval: data.pollInterval,
			subnetsFilter: data.subnetsFilter,
			retentionMinutes: data.retentionMinutes,
			excludedHostnames: data.excludedHostnames,
			reconciliationEnabled: data.reconciliationEnabled,
			deleteOrphanRecords: data.deleteOrphanRecords,
			journalRetentionDays: data.journalRetentionDays,
			technitium: {
				enabled: data.technitium.enabled,
				baseUrl: data.technitium.baseUrl,
				zone: data.technitium.zone,
				primaryNode: data.technitium.primaryNode,
				recordTtlSeconds: data.technitium.recordTtlSeconds,
				createPtr: data.technitium.createPtr,
			},
		});
	}, [settings.data]);

	if (settings.isLoading || !form) return <Loading />;
	if (settings.isError) return <QueryError error={settings.error} />;

	const patch = (changes: Partial<SettingsWriteDto>) =>
		setForm((current) => (current ? { ...current, ...changes } : current));
	const patchTechnitium = (changes: Partial<SettingsWriteDto["technitium"]>) =>
		setForm((current) =>
			current ? { ...current, technitium: { ...current.technitium, ...changes } } : current,
		);

	const submit = () => {
		save.mutate(
			{
				...form,
				pollInterval: toTimespan(pollSeconds),
				// Empty means "keep the stored token", matching the backend's rule.
				technitium: { ...form.technitium, apiToken: apiToken || undefined },
			},
			{
				onSuccess: () => {
					setApiToken("");
					enqueueSnackbar("Réglages enregistrés", { variant: "success" });
				},
				onError: (error) =>
					enqueueSnackbar(
						error instanceof Error ? error.message : "Échec de l'enregistrement",
						{ variant: "error" },
					),
			},
		);
	};

	return (
		<Stack spacing={3}>
			<PageTitle>Réglages</PageTitle>

			<NodesCard />

			<Card>
				<CardContent>
					<Typography variant="h6" sx={{ mb: 2 }}>
						Collecte
					</Typography>
					<Stack spacing={2}>
						<TextField
							label="Intervalle de collecte (secondes)"
							type="number"
							value={pollSeconds}
							onChange={(event) => setPollSeconds(Number(event.target.value))}
							helperText="Plancher à 15 secondes côté serveur"
						/>
						<SubnetsEditor
							value={form.subnetsFilter}
							onChange={(subnetsFilter) => patch({ subnetsFilter })}
						/>
						<TextField
							label="Rétention (minutes)"
							type="number"
							value={form.retentionMinutes}
							onChange={(event) =>
								patch({ retentionMinutes: Number(event.target.value) })
							}
							helperText="Durée pendant laquelle un hôte disparu conserve son enregistrement DNS"
						/>
						<TextField
							label="Hôtes exclus du DNS (un par ligne)"
							multiline
							minRows={2}
							value={form.excludedHostnames.join("\n")}
							onChange={(event) =>
								patch({
									excludedHostnames: event.target.value
										.split("\n")
										.map((s) => s.trim())
										.filter(Boolean),
								})
							}
						/>
						<TextField
							label="Rétention des journaux (jours)"
							type="number"
							value={form.journalRetentionDays}
							onChange={(event) =>
								patch({ journalRetentionDays: Number(event.target.value) })
							}
							helperText="Historique, santé et passages DNS. Modifie l'index TTL au prochain démarrage."
						/>
					</Stack>
				</CardContent>
			</Card>

			<Card>
				<CardContent>
					<Typography variant="h6" sx={{ mb: 2 }}>
						Technitium
					</Typography>

					<Alert severity="warning" sx={{ mb: 2 }}>
						<AlertTitle>Avant d&apos;activer la réconciliation</AlertTitle>
						Laissez-la désactivée pour un premier passage, ouvrez l&apos;écran DNS et
						vérifiez que le diff proposé ne contient aucun enregistrement posé à la
						main.
					</Alert>

					<Stack spacing={2}>
						<FormControlLabel
							control={
								<Checkbox
									checked={form.technitium.enabled}
									onChange={(event) =>
										patchTechnitium({ enabled: event.target.checked })
									}
								/>
							}
							label="Activer l'export Technitium"
						/>
						<TextField
							label="URL du service web"
							value={form.technitium.baseUrl}
							onChange={(event) => patchTechnitium({ baseUrl: event.target.value })}
							helperText="Par exemple http://ely-dns-01.elylan:5380"
						/>
						<TextField
							label="Jeton d'API"
							type="password"
							value={apiToken}
							onChange={(event) => setApiToken(event.target.value)}
							helperText={
								settings.data?.technitium.hasApiToken
									? "Un jeton est enregistré. Laisser vide pour le conserver."
									: "Aucun jeton enregistré."
							}
						/>
						<TextField
							label="Zone"
							value={form.technitium.zone}
							onChange={(event) => patchTechnitium({ zone: event.target.value })}
						/>
						<TextField
							label="Nœud primaire"
							value={form.technitium.primaryNode ?? ""}
							onChange={(event) =>
								patchTechnitium({ primaryNode: event.target.value || null })
							}
							helperText="Les écritures ne visent que le primaire ; la réplication reste à la charge du cluster"
						/>
						<TextField
							label="TTL des enregistrements (secondes)"
							type="number"
							value={form.technitium.recordTtlSeconds}
							onChange={(event) =>
								patchTechnitium({ recordTtlSeconds: Number(event.target.value) })
							}
						/>
						<FormControlLabel
							control={
								<Checkbox
									checked={form.technitium.createPtr}
									onChange={(event) =>
										patchTechnitium({ createPtr: event.target.checked })
									}
								/>
							}
							label="Créer les enregistrements PTR"
						/>
						<FormControlLabel
							control={
								<Checkbox
									checked={form.reconciliationEnabled}
									onChange={(event) =>
										patch({ reconciliationEnabled: event.target.checked })
									}
								/>
							}
							label="Activer la réconciliation (écrit réellement dans la zone)"
						/>
						<FormControlLabel
							control={
								<Checkbox
									checked={form.deleteOrphanRecords}
									onChange={(event) =>
										patch({ deleteOrphanRecords: event.target.checked })
									}
								/>
							}
							label="Supprimer les enregistrements orphelins portant le marqueur de cet outil"
						/>
					</Stack>
				</CardContent>
			</Card>

			<Box sx={{ display: "flex", justifyContent: "flex-end" }}>
				<Button variant="contained" size="large" onClick={submit} loading={save.isPending}>
					Enregistrer
				</Button>
			</Box>
		</Stack>
	);
}
