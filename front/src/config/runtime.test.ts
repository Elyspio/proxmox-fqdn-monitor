import { describe, expect, it } from "vitest";
import { resolveRuntimeConfig } from "./runtime";

const origin = "https://monitor.elyspio.fr";

describe("resolveRuntimeConfig", () => {
	it("reports an error when no authority is configured", () => {
		const { error } = resolveRuntimeConfig(undefined, {}, origin);

		expect(error).toBeDefined();
	});

	it("reports an error when the authority is not an absolute http URL", () => {
		const { error } = resolveRuntimeConfig(
			{ oauth: { authority: "auth.elyspio.fr", client_id: "proxmox-ip-monitor" } },
			{},
			origin,
		);

		expect(error).toBeDefined();
	});

	it("accepts a complete configuration", () => {
		const { config, error } = resolveRuntimeConfig(
			{
				oauth: {
					authority: "https://auth.elyspio.fr/realms/internal",
					client_id: "proxmox-ip-monitor",
				},
			},
			{},
			origin,
		);

		expect(error).toBeUndefined();
		expect(config.oauth.authority).toBe("https://auth.elyspio.fr/realms/internal");
		expect(config.oauth.redirect_uri).toBe(`${origin}/login/callback`);
	});

	it("lets Aspire environment variables win over conf.js", () => {
		// This is what makes `aspire run` work against the local Keycloak without editing conf.js.
		const { config } = resolveRuntimeConfig(
			{ oauth: { authority: "https://prod/realms/internal", client_id: "prod-client" } },
			{
				VITE_OIDC_AUTHORITY: "http://localhost:8080/realms/proxmox-ip-monitor",
				VITE_OIDC_CLIENT_ID: "proxmox-ip-monitor",
			},
			origin,
		);

		expect(config.oauth.authority).toBe("http://localhost:8080/realms/proxmox-ip-monitor");
		expect(config.oauth.client_id).toBe("proxmox-ip-monitor");
	});

	it("defaults the API endpoint to the same origin", () => {
		const { config } = resolveRuntimeConfig(undefined, {}, origin);

		expect(config.endpoints.core).toBe("");
	});
});
