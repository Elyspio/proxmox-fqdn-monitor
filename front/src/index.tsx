import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SnackbarProvider } from "notistack";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import "@fontsource-variable/space-grotesk";
import "@fontsource-variable/jetbrains-mono";

import { ThemeModeProvider } from "@/config/themeMode";
import { AppAuthProvider } from "@/core/auth/AuthProvider";
import { AppLayout } from "@/view/layout/AppLayout";
import { Gate } from "@/view/layout/Gate";
import { HostsPage } from "@/view/hosts/HostsPage";
import { HistoryPage } from "@/view/history/HistoryPage";
import { HealthPage } from "@/view/health/HealthPage";
import { DnsPage } from "@/view/dns/DnsPage";
import { SettingsPage } from "@/view/settings/SettingsPage";

const queryClient = new QueryClient({
	defaultOptions: {
		queries: {
			// A 401/403 will not fix itself by retrying; the Gate handles those explicitly.
			retry: 1,
			refetchOnWindowFocus: false,
		},
	},
});

createRoot(document.getElementById("root")!).render(
	<StrictMode>
		<ThemeModeProvider>
			<SnackbarProvider
				maxSnack={3}
				anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
			>
				<QueryClientProvider client={queryClient}>
					<BrowserRouter>
						<AppAuthProvider>
							<Gate>
								<Routes>
									<Route element={<AppLayout />}>
										<Route path="/" element={<HostsPage />} />
										<Route path="/history" element={<HistoryPage />} />
										<Route path="/health" element={<HealthPage />} />
										<Route path="/dns" element={<DnsPage />} />
										<Route path="/settings" element={<SettingsPage />} />
										{/* The OIDC callback lands here; onSigninCallback has already cleaned the URL. */}
										<Route path="/login/callback" element={<HostsPage />} />
									</Route>
								</Routes>
							</Gate>
						</AppAuthProvider>
					</BrowserRouter>
				</QueryClientProvider>
			</SnackbarProvider>
		</ThemeModeProvider>
	</StrictMode>,
);
