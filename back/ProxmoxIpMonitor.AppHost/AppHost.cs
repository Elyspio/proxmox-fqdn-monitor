// Aspire AppHost — MongoDB + Keycloak + the API + the Vite front, for local development.

using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.Services.AddLogging(x => x.AddSimpleConsole(l => l.SingleLine = true));

var isE2E = string.Equals(builder.Configuration["E2E"], "true", StringComparison.OrdinalIgnoreCase);

var mongo = builder.AddMongoDB("mongo").WithImage("mongo:8.0.4");
if (!isE2E) mongo.WithDataVolume();
var mongodb = mongo.AddDatabase("proxmox-ip-monitor");

// Keycloak on a pinned port so the issuer URL is identical for the browser and the API.
// The realm seeds the admin role, a "dev" user holding it, and a "noroles" user that does not —
// the second one is how the access-denied path stays exercised.
var keycloak = builder.AddKeycloak("keycloak", 8080);
if (!isE2E) keycloak.WithDataVolume();
keycloak.WithRealmImport("./Realms");

var authority = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/proxmox-ip-monitor");
const string clientId = "proxmox-ip-monitor";

// The master key must survive individual `aspire run` invocations: the Mongo data volume above
// persists the Data Protection key ring and the encrypted API tokens across restarts, so a fresh
// key each run would leave every stored secret undecryptable (see AGENTS.md > Secrets). The dev
// key is generated once and cached under the user's local app data — outside the repo, and scoped
// to this machine just like the Mongo volume. Deleting that file is equivalent to wiping the
// volume: stored tokens must be re-entered. Deployment supplies a real key through a Helm Secret.
// E2E runs use a throwaway (volume-less) Mongo, so a per-run key is correct there.
var masterKey = isE2E
	? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
	: GetOrCreateDevMasterKey();

var api = builder.AddProject<ProxmoxIpMonitor_Web>("api")
	.WithReference(mongodb, "MongoDB")
	.WaitFor(mongodb)
	.WaitFor(keycloak)
	.WithEnvironment("Auth__Authority", authority)
	.WithEnvironment("Auth__Audience", clientId)
	.WithEnvironment("Auth__AdminRole", "proxmox-ip-monitor-admin")
	.WithEnvironment("DataProtection__MasterKey", masterKey);

// Vite dev server. Pinned to 5173 and un-proxied: a stable origin is what makes the OIDC
// redirect URIs in the realm valid, and it keeps the HMR websocket working.
builder.AddViteApp("front", "../../front")
	.WithPnpm()
	.WithReference(api)
	.WithEnvironment("VITE_API_TARGET", api.GetEndpoint("https"))
	.WithEnvironment("VITE_OIDC_AUTHORITY", authority)
	.WithEnvironment("VITE_OIDC_CLIENT_ID", clientId)
	.WithEndpoint("http", endpoint =>
	{
		endpoint.Port = 5173;
		endpoint.TargetPort = 5173;
		endpoint.IsProxied = false;
	})
	.WaitFor(api);

builder.Build().Run();

// Reuse a per-developer key across runs so the persistent Mongo volume stays decryptable.
static string GetOrCreateDevMasterKey()
{
	var dir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"ProxmoxIpMonitor");
	var path = Path.Combine(dir, "dev-dataprotection-master-key");

	if (File.Exists(path)) return File.ReadAllText(path).Trim();

	Directory.CreateDirectory(dir);
	var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
	File.WriteAllText(path, key);
	return key;
}
