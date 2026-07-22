import { defineConfig } from "vite-plus";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";

// The .NET API serves the SPA in production; in dev, /api is proxied to the local API.
const API_TARGET = process.env.VITE_API_TARGET ?? "https://localhost:7193";

export default defineConfig({
	plugins: [react()],
	resolve: {
		alias: {
			"@": fileURLToPath(new URL("./src", import.meta.url)),
		},
	},
	server: {
		port: 5173,
		// Stable OIDC origin: fail rather than fall back to another port, since the redirect
		// URIs registered in Keycloak are pinned to 5173.
		strictPort: true,
		proxy: {
			"/api": { target: API_TARGET, changeOrigin: true, secure: false },
		},
	},
	build: {
		outDir: "dist",
		sourcemap: true,
	},
	fmt: {
		useTabs: true,
		tabWidth: 4,
	},
	lint: {
		ignorePatterns: ["dist/**"],
	},
});
