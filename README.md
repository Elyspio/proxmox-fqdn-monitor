# Proxmox IP Monitor

Web application that tracks the IP addresses of every VM and LXC container across Proxmox nodes,
keeps a history of the changes, and owns the matching A records in a Technitium DNS zone.

It replaces the previous `proxmox-fqdn-exporter` binary: instead of a one-shot executable deployed
on each hypervisor, a single service in Kubernetes polls the Proxmox REST API, consolidates the
result in MongoDB, and exposes it behind Keycloak.

## What it does

- Polls every configured Proxmox node for the hypervisor's own address, its running VMs (through the
  QEMU guest agent) and its running containers.
- Keeps a consolidated snapshot, so two independent hypervisors produce one view.
- Records every appearance, address change, rename and disappearance, with a bounded history.
- Reports collection health per node: unreachable nodes, VMs without a responding agent, guests with
  no address inside the configured subnets.
- Reconciles a Technitium primary zone: writes the records it owns, and deletes the ones that no
  longer have a host behind them.

### Record ownership

Every record this tool writes carries `proxmox-ip-monitor` in its Technitium comments field.
Reconciliation only ever deletes records carrying that marker, so records maintained by hand in the
same zone are structurally out of reach. On the first run, a record that matches the desired state
but has no marker is rewritten once, which adopts records left behind by the previous exporter.

Record aging (`expiryTtl`) is deliberately not used: it forces a rewrite on every single run, which
is what inflated the primary zone's IXFR history. Stale records are removed explicitly instead.

## Requirements

- .NET 10 SDK, pnpm 11, Docker (for the Aspire services). Node 24 matches the container build.
- MongoDB.
- A Keycloak realm. Every route, reads included, requires a realm role — see below.
- One Proxmox API token per node.

### Proxmox permissions

`PVEAuditor` alone is **not** sufficient: reading a VM's addresses goes through
`/nodes/{node}/qemu/{vmid}/agent/network-get-interfaces`, which requires `VM.Monitor`.

Create a role granting `Sys.Audit`, `VM.Audit` and `VM.Monitor`, then a token with privilege
separation enabled:

```bash
pveum role add IpMonitor --privs "Sys.Audit,VM.Audit,VM.Monitor"
pveum user add monitor@pve
pveum acl modify / --users monitor@pve --roles IpMonitor
pveum user token add monitor@pve ip-monitor --privsep 1
```

The token id (`monitor@pve!ip-monitor`) and its secret are entered in the settings screen.

## Configuration

Almost everything is edited in the UI and stored in MongoDB: nodes and their tokens, subnet filters,
retention, poll interval, and the Technitium settings. Only the bootstrap values live in
configuration:

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:MongoDB` | Database connection string |
| `Auth:Authority` | Realm issuer, e.g. `https://auth.elyspio.fr/realms/internal` |
| `Auth:Audience` | Keycloak client id |
| `Auth:AdminRole` | Realm role required on every route (default `proxmox-ip-monitor-admin`) |
| `DataProtection:MasterKey` | Base64 32-byte key wrapping the token encryption key ring |

Generate the master key with `openssl rand -base64 32`. It encrypts the Data Protection key ring
stored in MongoDB, which in turn encrypts the API tokens. **Losing it makes every stored token
unreadable and they must be re-entered** — that blast radius is intended: a database dump alone
grants no access to the hypervisors.

The API loads `appsettings.json`, plus optional `appsettings.docker.json` (a mounted secret in
deployment) and a gitignored `appsettings.Local.json`.

Frontend runtime configuration comes from `front/public/conf.js`, overwritten at deploy time.

## Development

```bash
aspire run
```

Starts MongoDB, Keycloak (port 8080), the API and Vite (port 5173). The realm in
`back/ProxmoxIpMonitor.AppHost/Realms/` seeds two accounts: `dev` / `dev`, holding the admin role,
and `noroles` / `noroles`, which does not — the second one exercises the access-denied path.

```bash
dotnet build back/ProxmoxIpMonitor.slnx
dotnet test back/ProxmoxIpMonitor.slnx
```

```bash
cd front
pnpm install --frozen-lockfile
pnpm dev
pnpm check      # runs `vp check --fix`; it mutates files, review the diff
pnpm test
pnpm build
```

## First run against a live zone

Reconciliation is off by default. Turn it on only after checking the diff:

1. Declare a node in **Réglages**, use **Tester la connexion**, then **Collecter**.
2. Configure Technitium, leaving *Activer la réconciliation* unchecked.
3. Open **DNS** and confirm the proposed diff contains none of your hand-maintained records —
   they should appear under *Enregistrements hors gestion*.
4. Enable reconciliation, and enable orphan deletion separately once you trust the orphan list.

## Deployment

```bash
pwsh deploy/build/build.ps1
```

Builds the single-container image (frontend into `wwwroot`, API on port 4000), pushes it, and
deploys the Helm chart from `infrastructure-elylan`. Pass `-ChartPath` if the infrastructure
repository sits elsewhere, or `-SkipDeploy` to only publish the image.

Run a single replica with the `Recreate` strategy. The collector and the DNS writer are singletons;
two replicas would issue overlapping writes to the same zone.

## Project structure

```text
back/
  ProxmoxIpMonitor.Abstractions/     Models and ports. Depends on nothing.
  ProxmoxIpMonitor.Core/             Collection loop, snapshot diffing, retention.
  ProxmoxIpMonitor.Adapters.Proxmox/ Proxmox REST client.
  ProxmoxIpMonitor.Adapters.Dns/     Technitium provider.
  ProxmoxIpMonitor.Adapters.MongoDB/ Repositories and secret protection.
  ProxmoxIpMonitor.Web/              API, auth, composition root, SPA hosting.
  ProxmoxIpMonitor.AppHost/          Aspire local stack.
front/                               React 19, Vite+, MUI, TanStack Query.
deploy/build/                        Container image and deployment script.
```

## License

See [LICENSE](./LICENSE).
