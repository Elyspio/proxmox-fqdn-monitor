import { AppBar, Box, Button, Container, Tab, Tabs, Toolbar, Typography } from "@mui/material";
import RefreshIcon from "@mui/icons-material/Refresh";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import { useSnackbar } from "notistack";
import { useAppAuth } from "@/core/auth/AuthProvider";
import { useCollectNow } from "@/core/api/mutations";
import { ThemeToggle } from "@/view/layout/ThemeToggle";

const tabs = [
	{ label: "Hôtes", path: "/" },
	{ label: "Historique", path: "/history" },
	{ label: "Santé", path: "/health" },
	{ label: "DNS", path: "/dns" },
	{ label: "Réglages", path: "/settings" },
];

export function AppLayout() {
	const auth = useAppAuth();
	const location = useLocation();
	const collect = useCollectNow();
	const { enqueueSnackbar } = useSnackbar();

	const active = tabs.findIndex((tab) =>
		tab.path === "/" ? location.pathname === "/" : location.pathname.startsWith(tab.path),
	);

	const runCollection = () =>
		collect.mutate(undefined, {
			onSuccess: () => enqueueSnackbar("Collecte terminée", { variant: "success" }),
			onError: (error) =>
				enqueueSnackbar(error instanceof Error ? error.message : "Échec de la collecte", {
					variant: "error",
				}),
		});

	return (
		<Box sx={{ minHeight: "100vh", bgcolor: "background.default" }}>
			<AppBar
				position="sticky"
				color="transparent"
				elevation={0}
				sx={{ backdropFilter: "blur(12px)", borderBottom: 1, borderColor: "divider" }}
			>
				<Toolbar>
					<Typography variant="h6" sx={{ fontWeight: 700, mr: 4 }}>
						Proxmox IP Monitor
					</Typography>

					<Tabs value={active === -1 ? false : active} sx={{ flexGrow: 1 }}>
						{tabs.map((tab) => (
							<Tab
								key={tab.path}
								label={tab.label}
								component={NavLink}
								to={tab.path}
							/>
						))}
					</Tabs>

					<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
						<Button
							startIcon={<RefreshIcon />}
							onClick={runCollection}
							loading={collect.isPending}
							variant="outlined"
							size="small"
						>
							Collecter
						</Button>
						<ThemeToggle />
						{auth.name && (
							<Typography variant="body2" color="text.secondary">
								{auth.name}
							</Typography>
						)}
						<Button size="small" onClick={auth.signOut}>
							Déconnexion
						</Button>
					</Box>
				</Toolbar>
			</AppBar>

			<Container maxWidth="xl" sx={{ py: 4 }}>
				<Outlet />
			</Container>
		</Box>
	);
}
