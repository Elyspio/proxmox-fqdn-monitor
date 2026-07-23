import { Box, Button, IconButton, TextField, Tooltip, Typography } from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutlined";
import type { Subnet } from "@/core/api/types";

const columns = "1.6fr 1.6fr 40px";

/**
 * Structured editor for the configured subnets. Each row is a CIDR (required, validated server-side)
 * and an optional human name. The name labels the VLAN a host sits in on the Hôtes page; the VLAN
 * number itself is read from the guest's real NIC tag, so it is not entered here.
 */
export function SubnetsEditor({
	value,
	onChange,
}: {
	value: Subnet[];
	onChange: (next: Subnet[]) => void;
}) {
	const update = (index: number, changes: Partial<Subnet>) =>
		onChange(value.map((subnet, i) => (i === index ? { ...subnet, ...changes } : subnet)));

	const remove = (index: number) => onChange(value.filter((_, i) => i !== index));

	const add = () => onChange([...value, { cidr: "", label: null }]);

	return (
		<Box>
			<Typography variant="subtitle2" sx={{ mb: 1 }}>
				Sous-réseaux
			</Typography>

			{value.length > 0 && (
				<Box
					sx={{
						display: "grid",
						gridTemplateColumns: columns,
						gap: 1,
						px: 1,
						mb: 0.5,
						color: "text.secondary",
					}}
				>
					<Typography variant="caption">CIDR</Typography>
					<Typography variant="caption">Nom</Typography>
					<span />
				</Box>
			)}

			<Box sx={{ display: "flex", flexDirection: "column", gap: 1 }}>
				{value.map((subnet, index) => (
					// Index key: rows have no stable id and are only ever reordered by add/remove at the ends.
					<Box
						key={index}
						sx={{
							display: "grid",
							gridTemplateColumns: columns,
							gap: 1,
							alignItems: "center",
						}}
					>
						<TextField
							size="small"
							placeholder="10.0.0.0/24"
							value={subnet.cidr}
							onChange={(event) => update(index, { cidr: event.target.value })}
						/>
						<TextField
							size="small"
							placeholder="Services"
							value={subnet.label ?? ""}
							onChange={(event) =>
								update(index, { label: event.target.value || null })
							}
						/>
						<Tooltip title="Supprimer ce sous-réseau">
							<IconButton size="small" onClick={() => remove(index)}>
								<DeleteOutlineIcon fontSize="small" />
							</IconButton>
						</Tooltip>
					</Box>
				))}
			</Box>

			<Button startIcon={<AddIcon />} onClick={add} size="small" sx={{ mt: 1 }}>
				Ajouter un sous-réseau
			</Button>
			<Typography variant="caption" color="text.secondary" sx={{ display: "block", mt: 1 }}>
				Le CIDR sert à retenir l&apos;adresse d&apos;un hôte et à nommer son VLAN sur la
				page Hôtes. Le nom est facultatif.
			</Typography>
		</Box>
	);
}
