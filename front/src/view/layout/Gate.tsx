import { Alert, AlertTitle, Box, Button, Paper, Stack, Typography } from "@mui/material";
import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import axios from "axios";
import { runtimeConfigError } from "@/config/runtime";
import { http } from "@/core/api/client";
import { useAppAuth } from "@/core/auth/AuthProvider";
import { Loading } from "@/view/components/Common";

function Centered({ children }: { children: ReactNode }) {
	return (
		<Box sx={{ minHeight: "100vh", display: "grid", placeItems: "center", bgcolor: "background.default", p: 3 }}>
			<Paper sx={{ p: 4, maxWidth: 560 }}>{children}</Paper>
		</Box>
	);
}

/**
 * Gates the whole app on three things, in order: a usable runtime configuration, an
 * authenticated session, and the backend actually accepting the caller.
 *
 * Authorisation is decided by the API, not by inspecting the token here — a 403 on the probe
 * is what produces the refusal screen. That keeps the required role defined in exactly one
 * place instead of two that can drift apart.
 */
export function Gate({ children }: { children: ReactNode }) {
	const auth = useAppAuth();

	const probe = useQuery({
		queryKey: ["access-probe"],
		queryFn: async () => (await http.get("/api/settings")).data,
		enabled: auth.isAuthenticated,
		retry: false,
	});

	if (runtimeConfigError) {
		return (
			<Centered>
				<Alert severity="error">
					<AlertTitle>Configuration incomplète</AlertTitle>
					{runtimeConfigError}
					<Typography variant="body2" sx={{ mt: 1 }}>
						Vérifiez le fichier <code>conf.js</code> servi par l&apos;application.
					</Typography>
				</Alert>
			</Centered>
		);
	}

	if (auth.isLoading) return <Loading />;

	if (auth.error) {
		return (
			<Centered>
				<Alert severity="error">
					<AlertTitle>Échec de l&apos;authentification</AlertTitle>
					{auth.error}
				</Alert>
				<Button sx={{ mt: 2 }} variant="contained" onClick={auth.signIn}>
					Réessayer
				</Button>
			</Centered>
		);
	}

	if (!auth.isAuthenticated) {
		return (
			<Centered>
				<Stack spacing={2}>
					<Typography variant="h5" sx={{ fontWeight: 700 }}>
						Proxmox IP Monitor
					</Typography>
					<Typography color="text.secondary">
						Cette application expose des jetons d&apos;hyperviseur et pilote une zone DNS. Une connexion
						est requise.
					</Typography>
					<Button variant="contained" onClick={auth.signIn}>
						Se connecter
					</Button>
				</Stack>
			</Centered>
		);
	}

	if (probe.isLoading) return <Loading />;

	if (axios.isAxiosError(probe.error) && probe.error.response?.status === 403) {
		return (
			<Centered>
				<Alert severity="warning">
					<AlertTitle>Accès refusé</AlertTitle>
					Votre compte est authentifié mais ne porte pas le rôle requis. Demandez l&apos;attribution du
					rôle d&apos;administration de cette application dans Keycloak.
				</Alert>
				<Button sx={{ mt: 2 }} onClick={auth.signOut}>
					Changer de compte
				</Button>
			</Centered>
		);
	}

	if (probe.isError) {
		return (
			<Centered>
				<Alert severity="error">
					<AlertTitle>API injoignable</AlertTitle>
					{probe.error instanceof Error ? probe.error.message : String(probe.error)}
				</Alert>
			</Centered>
		);
	}

	return <>{children}</>;
}
