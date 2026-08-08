// Aspire AppHost — MongoDB + Keycloak + the API + the Vite front, for local development.

using System.Net;
using System.Net.Sockets;
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

// Both Keycloak and the Vite front run on ports picked randomly once and then cached per machine
// (see GetOrCreateDevPort), rather than the pinned 8080 / 5173. This lets this AppHost run next to
// other Aspire projects without colliding on those well-known ports, while staying stable across
// runs — which matters because Keycloak's data volume persists the imported realm, so the realm's
// redirect URIs (materialised with the front port below) must keep matching on every later run.
var keycloakPort = GetOrCreateDevPort("keycloak");
var frontPort = GetOrCreateDevPort("front");

// Keycloak's issuer URL is derived from the endpoint below and handed to both the browser and the
// API, so it stays consistent. The realm seeds the admin role, a "dev" user holding it, and a
// "noroles" user that does not — the second one is how the access-denied path stays exercised. The
// realm import is materialised with the actual front origin so its redirect URIs are exact:
// Keycloak matches redirect URIs exactly (only a trailing path wildcard is allowed, never the port).
var keycloak = builder.AddKeycloak("keycloak", keycloakPort);
if (!isE2E) keycloak.WithDataVolume();
keycloak.WithRealmImport(MaterializeRealmImport($"http://localhost:{frontPort}"));

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

// Vite dev server on the cached random port above, un-proxied: un-proxied keeps the HMR websocket
// working, and the non-pinned port lets this run alongside other Vite dev servers without colliding
// on 5173. The realm's redirect URIs were materialised for exactly this origin.
builder.AddViteApp("front", "../../front")
	.WithPnpm()
	.WithReference(api)
	.WithEnvironment("VITE_API_TARGET", api.GetEndpoint("https"))
	.WithEnvironment("VITE_OIDC_AUTHORITY", authority)
	.WithEnvironment("VITE_OIDC_CLIENT_ID", clientId)
	.WithEndpoint("http", endpoint =>
	{
		endpoint.Port = frontPort;
		endpoint.TargetPort = frontPort;
		endpoint.IsProxied = false;
	})
	.WaitFor(api);

builder.Build().Run();

// Pick a free host port once and cache it per developer, so it stays stable across `aspire run`
// invocations (the persisted Keycloak realm and the front's OIDC redirect URIs depend on that) but
// still avoids the well-known ports other Aspire projects reach for. Deleting the cache file re-rolls
// the port; the realm re-import on the next fresh Keycloak volume will pick up the new value.
static int GetOrCreateDevPort(string name)
{
	var path = Path.Combine(DevStateDir(), $"dev-port-{name}");
	if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var cached))
		return cached;

	Directory.CreateDirectory(DevStateDir());
	var port = GetFreePort();
	File.WriteAllText(path, port.ToString());
	return port;
}

// Ask the OS for an unused TCP port by binding to port 0. The socket is released immediately; the
// small race between release and the child process binding it is acceptable for local development.
static int GetFreePort()
{
	using var listener = new TcpListener(IPAddress.Loopback, 0);
	listener.Start();
	var port = ((IPEndPoint)listener.LocalEndpoint).Port;
	listener.Stop();
	return port;
}

// Write the realm import into a temp directory with the front origin substituted for its placeholder,
// so Keycloak imports exact redirect URIs. Returns the directory to hand to WithRealmImport.
static string MaterializeRealmImport(string frontOrigin)
{
	const string fileName = "proxmox-ip-monitor-realm.json";
	var template = File.ReadAllText(Path.Combine("Realms", fileName));
	var content = template.Replace("__FRONT_ORIGIN__", frontOrigin);

	var outDir = Path.Combine(DevStateDir(), "realm-import");
	Directory.CreateDirectory(outDir);
	File.WriteAllText(Path.Combine(outDir, fileName), content);
	return outDir;
}

// Reuse a per-developer key across runs so the persistent Mongo volume stays decryptable.
static string GetOrCreateDevMasterKey()
{
	var path = Path.Combine(DevStateDir(), "dev-dataprotection-master-key");

	if (File.Exists(path)) return File.ReadAllText(path).Trim();

	Directory.CreateDirectory(DevStateDir());
	var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
	File.WriteAllText(path, key);
	return key;
}

// Per-developer, per-machine state that must survive individual `aspire run` invocations: outside
// the repo, alongside the Mongo data volume's lifetime. See GetOrCreateDevMasterKey / GetOrCreateDevPort.
static string DevStateDir() => Path.Combine(
	Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
	"ProxmoxIpMonitor");
