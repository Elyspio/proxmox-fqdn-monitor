import { useMemo, useState } from "react";
import {
	Chip,
	IconButton,
	Paper,
	Stack,
	Table,
	TableBody,
	TableCell,
	TableContainer,
	TableHead,
	TableRow,
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

export function HostsPage() {
	const hosts = useHosts();
	const patch = usePatchHost();
	const [filter, setFilter] = useState("");

	const rows = useMemo(() => {
		const needle = filter.trim().toLowerCase();
		const all = hosts.data ?? [];
		if (!needle) return all;
		return all.filter(
			(host) =>
				host.hostname.toLowerCase().includes(needle) ||
				(host.ip ?? "").includes(needle) ||
				host.nodeName.toLowerCase().includes(needle),
		);
	}, [hosts.data, filter]);

	if (hosts.isLoading) return <Loading />;
	if (hosts.isError) return <QueryError error={hosts.error} />;

	return (
		<Stack spacing={3}>
			<SpacedRow>
				<PageTitle>Hôtes</PageTitle>
				<TextField
					size="small"
					placeholder="Filtrer par nom, IP ou nœud"
					value={filter}
					onChange={(event) => setFilter(event.target.value)}
					sx={{ width: 320 }}
				/>
			</SpacedRow>

			<TableContainer component={Paper}>
				<Table size="small">
					<TableHead>
						<TableRow>
							<TableCell>Nom</TableCell>
							<TableCell>Adresse</TableCell>
							<TableCell>Type</TableCell>
							<TableCell>Nœud</TableCell>
							<TableCell>VMID</TableCell>
							<TableCell>État</TableCell>
							<TableCell>Vu pour la dernière fois</TableCell>
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
