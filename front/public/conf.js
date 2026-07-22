// Frontend RUNTIME configuration — read by src/config/runtime.ts at startup.
// This file (dev defaults) is served as-is by Vite. In production, the deployment
// overwrites /app/wwwroot/conf.js with the real values.
window.proxmoxIpMonitor = window.proxmoxIpMonitor || {};
window.proxmoxIpMonitor.config = {
	// "" = same origin. In dev, Vite proxies /api to the .NET API.
	endpoints: {
		core: "",
	},
	oauth: {
		// OIDC authority, e.g. https://auth.elyspio.fr/realms/internal
		authority: "",
		client_id: "proxmox-ip-monitor",
		redirect_uri: window.location.origin + "/login/callback",
		post_logout_redirect_uri: window.location.origin + "/",
		response_type: "code",
		scope: "openid profile email",
	},
};
