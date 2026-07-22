/// <reference types="vite/client" />

export interface OAuthConfig {
	authority: string;
	client_id: string;
	redirect_uri: string;
	post_logout_redirect_uri: string;
	response_type: string;
	scope: string;
}

export interface RuntimeConfig {
	endpoints?: { core?: string };
	oauth?: Partial<OAuthConfig>;
}

// This file is a module (it exports types), so these declarations only reach the global
// scope through `declare global`. Without it, `ImportMetaEnv` here would be a second,
// unrelated interface and the Vite one would stay unaware of these variables.
declare global {
	interface ImportMetaEnv {
		/** Docker tag injected at image build time; absent in dev. */
		readonly VITE_APP_VERSION?: string;
		/** OIDC authority injected by the Aspire AppHost for local dev; absent otherwise. */
		readonly VITE_OIDC_AUTHORITY?: string;
		/** OIDC client id injected by the Aspire AppHost for local dev; absent otherwise. */
		readonly VITE_OIDC_CLIENT_ID?: string;
	}

	interface Window {
		proxmoxIpMonitor?: { config?: RuntimeConfig };
	}
}
