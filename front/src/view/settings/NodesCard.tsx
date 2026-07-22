import { useState } from "react";
import {
	Alert,
	Button,
	Card,
	CardContent,
	Checkbox,
	Dialog,
	DialogActions,
	DialogContent,
	DialogTitle,
	FormControlLabel,
	IconButton,
	Stack,
	Table,
	TableBody,
	TableCell,
	TableHead,
	TableRow,
	TextField,
	Typography,
} from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import EditIcon from "@mui/icons-material/Edit";
import { useSnackbar } from "notistack";
import { useNodes } from "@/core/api/queries";
import { useCreateNode, useDeleteNode, useTestNode, useUpdateNode } from "@/core/api/mutations";
import type { NodeDto, NodeWriteDto } from "@/core/api/types";
import { Empty, Mono, SpacedRow } from "@/view/components/Common";

const blank: NodeWriteDto = {
	displayName: "",
	baseUrl: "https://10.0.0.10:8006",
	nodeName: "",
	tokenId: "",
	tokenSecret: "",
	allowInvalidCertificate: true,
	enabled: true,
};

export function NodesCard() {
	const nodes = useNodes();
	const create = useCreateNode();
	const update = useUpdateNode();
	const remove = useDeleteNode();
	const test = useTestNode();
	const { enqueueSnackbar } = useSnackbar();

	const [editing, setEditing] = useState<{ id?: string; body: NodeWriteDto } | null>(null);

	const openCreate = () => setEditing({ body: { ...blank } });

	const openEdit = (node: NodeDto) =>
		setEditing({
			id: node.id,
			body: {
				displayName: node.displayName,
				baseUrl: node.baseUrl,
				nodeName: node.nodeName,
				tokenId: node.tokenId,
				// Left empty on purpose: the secret is never sent to the browser, and an empty
				// field means "keep the stored one".
				tokenSecret: "",
				allowInvalidCertificate: node.allowInvalidCertificate,
				enabled: node.enabled,
			},
		});

	const save = () => {
		if (!editing) return;
		const done = {
			onSuccess: () => {
				enqueueSnackbar("Nœud enregistré", { variant: "success" });
				setEditing(null);
			},
			onError: (error: unknown) =>
				enqueueSnackbar(error instanceof Error ? error.message : "Échec de l'enregistrement", { variant: "error" }),
		};

		if (editing.id) update.mutate({ id: editing.id, body: editing.body }, done);
		else create.mutate(editing.body, done);
	};

	const runTest = () => {
		if (!editing) return;
		test.mutate(
			{ id: editing.id, body: editing.body },
			{
				onSuccess: (result) =>
					enqueueSnackbar(result.message, { variant: result.success ? "success" : "error", autoHideDuration: 8000 }),
				onError: (error) =>
					enqueueSnackbar(error instanceof Error ? error.message : "Échec du test", { variant: "error" }),
			},
		);
	};

	const patch = (changes: Partial<NodeWriteDto>) =>
		setEditing((current) => (current ? { ...current, body: { ...current.body, ...changes } } : current));

	return (
		<Card>
			<CardContent>
				<SpacedRow sx={{ mb: 2 }}>
					<Typography variant="h6">Nœuds Proxmox</Typography>
					<Button variant="outlined" onClick={openCreate}>
						Ajouter
					</Button>
				</SpacedRow>

				<Alert severity="info" sx={{ mb: 2 }}>
					Le jeton doit porter un rôle donnant <Mono>Sys.Audit</Mono>, <Mono>VM.Audit</Mono> et{" "}
					<Mono>VM.Monitor</Mono>. <Mono>PVEAuditor</Mono> seul ne suffit pas : il n&apos;autorise pas les
					commandes de l&apos;agent invité.
				</Alert>

				<Table size="small">
					<TableHead>
						<TableRow>
							<TableCell>Nom</TableCell>
							<TableCell>URL</TableCell>
							<TableCell>Nœud PVE</TableCell>
							<TableCell>Jeton</TableCell>
							<TableCell>TLS</TableCell>
							<TableCell>Actif</TableCell>
							<TableCell align="right" />
						</TableRow>
					</TableHead>
					<TableBody>
						{nodes.data?.map((node) => (
							<TableRow key={node.id} hover>
								<TableCell>{node.displayName}</TableCell>
								<TableCell>
									<Mono>{node.baseUrl}</Mono>
								</TableCell>
								<TableCell>
									<Mono>{node.nodeName}</Mono>
								</TableCell>
								<TableCell>
									<Mono>{node.tokenId}</Mono>
									{!node.hasToken && (
										<Typography variant="caption" color="error" sx={{ display: "block" }}>
											aucun secret
										</Typography>
									)}
								</TableCell>
								<TableCell>{node.allowInvalidCertificate ? "Auto-signé accepté" : "Vérifié"}</TableCell>
								<TableCell>{node.enabled ? "Oui" : "Non"}</TableCell>
								<TableCell align="right">
									<IconButton size="small" onClick={() => openEdit(node)}>
										<EditIcon fontSize="small" />
									</IconButton>
									<IconButton
										size="small"
										onClick={() => {
											if (confirm(`Supprimer ${node.displayName} et tous ses hôtes ?`)) remove.mutate(node.id);
										}}
									>
										<DeleteIcon fontSize="small" />
									</IconButton>
								</TableCell>
							</TableRow>
						))}
					</TableBody>
				</Table>
				{nodes.data?.length === 0 && <Empty>Aucun nœud déclaré.</Empty>}
			</CardContent>

			<Dialog open={editing !== null} onClose={() => setEditing(null)} fullWidth maxWidth="sm">
				<DialogTitle>{editing?.id ? "Modifier le nœud" : "Ajouter un nœud"}</DialogTitle>
				<DialogContent>
					<Stack spacing={2} sx={{ mt: 1 }}>
						<TextField
							label="Nom affiché"
							value={editing?.body.displayName ?? ""}
							onChange={(event) => patch({ displayName: event.target.value })}
							fullWidth
						/>
						<TextField
							label="URL de l'API"
							helperText="Par exemple https://10.0.0.10:8006"
							value={editing?.body.baseUrl ?? ""}
							onChange={(event) => patch({ baseUrl: event.target.value })}
							fullWidth
						/>
						<TextField
							label="Nom du nœud dans Proxmox"
							helperText="Utilisé pour construire /nodes/{nom}/…"
							value={editing?.body.nodeName ?? ""}
							onChange={(event) => patch({ nodeName: event.target.value })}
							fullWidth
						/>
						<TextField
							label="Identifiant du jeton"
							helperText="Forme utilisateur@realm!nom-du-jeton"
							value={editing?.body.tokenId ?? ""}
							onChange={(event) => patch({ tokenId: event.target.value })}
							fullWidth
						/>
						<TextField
							label="Secret du jeton"
							type="password"
							helperText={editing?.id ? "Laisser vide pour conserver le secret enregistré" : "Requis"}
							value={editing?.body.tokenSecret ?? ""}
							onChange={(event) => patch({ tokenSecret: event.target.value })}
							fullWidth
						/>
						<FormControlLabel
							control={
								<Checkbox
									checked={editing?.body.allowInvalidCertificate ?? false}
									onChange={(event) => patch({ allowInvalidCertificate: event.target.checked })}
								/>
							}
							label="Accepter le certificat auto-signé"
						/>
						<FormControlLabel
							control={
								<Checkbox
									checked={editing?.body.enabled ?? true}
									onChange={(event) => patch({ enabled: event.target.checked })}
								/>
							}
							label="Interroger ce nœud"
						/>
					</Stack>
				</DialogContent>
				<DialogActions>
					<Button onClick={runTest} loading={test.isPending}>
						Tester la connexion
					</Button>
					<Button onClick={() => setEditing(null)}>Annuler</Button>
					<Button variant="contained" onClick={save} loading={create.isPending || update.isPending}>
						Enregistrer
					</Button>
				</DialogActions>
			</Dialog>
		</Card>
	);
}
