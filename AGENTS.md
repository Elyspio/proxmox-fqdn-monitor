# AGENTS.md

Guidance for coding agents working in this repository.

## Product

Proxmox IP Monitor polls the Proxmox REST API of one or more hypervisors, consolidates the IP
addresses of their VMs and containers in MongoDB, and reconciles a Technitium DNS zone against that
snapshot. It replaced a per-host AOT binary (`proxmox-fqdn-exporter`); there is no longer any code
running on the hypervisors, and no SSH anywhere.

## Repository map

```text
back/
  ProxmoxIpMonitor.Abstractions/     Models, ports, SubnetFilter, Result<T>. Depends on nothing.
  ProxmoxIpMonitor.Core/             Collector, SnapshotDiffer, DesiredRecordBuilder.
  ProxmoxIpMonitor.Adapters.Proxmox/ Only project that knows /api2/json and PVEAPIToken.
  ProxmoxIpMonitor.Adapters.Dns/     Technitium provider and its transport.
  ProxmoxIpMonitor.Adapters.MongoDB/ Repositories, TTL indexes, Data Protection.
  ProxmoxIpMonitor.Web/              API, auth, composition root, static SPA hosting.
  ProxmoxIpMonitor.AppHost/          Aspire: MongoDB, Keycloak, API, Vite.
front/
  src/config/    Runtime config resolution and theme
  src/core/api/  Axios client, TanStack Query hooks, shared API types
  src/core/auth/ OIDC bridge
  src/view/      Feature pages and shared UI components
deploy/build/    Single-container image and deployment script
```

Backend: .NET 10, ASP.NET Core, Aspire 13, MongoDB, xUnit v3.
Frontend: React 19, TypeScript, Vite+, MUI 9, TanStack Query, Axios, `react-oidc-context`, Vitest.

## Invariants

These are the properties the design rests on. Changing them is a product decision, not a refactor.

### DNS ownership

- Every record written carries `TechnitiumDnsProvider.OwnershipMarker` in its comments.
- Deletion targets **only** records carrying that marker. Records maintained by hand in the same
  zone must remain structurally unreachable. `TechnitiumDnsProviderTests` asserts this.
- `expiryTtl` is never sent. Record aging forces a rewrite on every run and inflates the primary
  zone's IXFR history; stale records are deleted explicitly instead.
- Writes target the configured primary node only; replication is Technitium's job.
- `ReconciliationEnabled = false` turns every pass into a dry run that still reports the diff.

### Collection

- A guest that cannot answer (no agent, no address in the configured subnets) becomes a
  `HostIssue` and the poll continues. Only a node-level failure fails the snapshot.
- Hosts belonging to a node that failed this cycle are left untouched — never expired. Expiring
  them would turn a transient API outage into a mass DNS deletion.
- A host that stops reporting keeps its stored address until retention expires, so a rebooting
  guest does not lose its record.
- `Collector` is a singleton and serialises every entry point on one semaphore. Do not add a second
  path that writes the snapshot or the zone.
- Reading a VM's addresses needs `VM.Monitor`, not just `VM.Audit`.

### Layering

- Controllers talk to services only (`INodeService`, `IHostService`, `ISettingsService`,
  `IDnsService`, `ICollector`). A controller never injects a repository, an adapter or
  `ISecretProtector` — raw secrets are protected inside the services.
- DTOs live in `ProxmoxIpMonitor.Abstractions/Transports`, models in `Abstractions/Models`,
  service interfaces in `Abstractions/Interfaces`. Implementations live in Core and are wired
  in `CoreModule`.

### Tracing

- Every behavioural class inherits its `Elyspio.Utils.Telemetry` base: controllers →
  `TracingController`, services → `TracingService`, adapters → `TracingAdapter`, repositories →
  `TracingRepository`. Static classes and `BackgroundService` implementations are exempt.
- Every public method opens `using var trace = LogX($"{Log.F(arg)}")` (`LogController`,
  `LogService`, `LogAdapter`, `LogRepository`). Private helpers and pure mappings do not.
- Never pass a secret — or a DTO that contains one (`NodeWriteDto`, `SettingsWriteDto`) —
  through `Log.F`. Log the safe fields (ids, display names) or nothing.
- Telemetry is bootstrapped in `Web/Program.cs` via `AppOpenTelemetryBuilder` +
  `UseSerilogWithTelemetry`; each product assembly is registered with `AddAssembly<T>` so its
  TracingX sources are exported. There is no ServiceDefaults project; health checks, service
  discovery and HTTP resilience live in `Web/Hosting/HostingModule.cs`.

### Secrets

- API tokens are encrypted with `ISecretProtector` before reaching MongoDB.
- The API never returns a secret. DTOs expose `hasToken`; an empty field on write means unchanged.
- `DataProtection:MasterKey` must be present and 32 bytes. Never generate one as a fallback —
  a silently invented key makes every stored token unreadable on the next restart.

### Auth

- One policy, applied as the default authorization policy: authenticated **and** holding the realm
  role. There is no read-only tier.
- Keycloak nests realm roles under `realm_access.roles`; `AuthModule` flattens them into role claims.
- The frontend does not decode the token to decide what to render. It probes the API and renders the
  refusal, so the required role has a single definition.

## Development

```bash
aspire run                                  # Mongo + Keycloak + API + Vite
dotnet build back/ProxmoxIpMonitor.slnx
dotnet test back/ProxmoxIpMonitor.slnx
```

```bash
cd front
pnpm install --frozen-lockfile
pnpm check    # `vp check --fix` mutates files; review the diff
pnpm test
pnpm build
```

## Testing expectations

- Collection, retention or diffing changes: extend `ProxmoxIpMonitor.Core.Tests`.
- Proxmox parsing or Technitium payload changes: extend `ProxmoxIpMonitor.Adapters.Tests` and assert
  the generated payloads precisely — that comments are present and `expiryTtl` is not.
- No test may require a real Proxmox node or DNS server. Drive the adapters through
  `FakeHttpMessageHandler`.
- Frontend runtime config changes: extend `front/src/config/runtime.test.ts`.

## Code style

- `.editorconfig` is authoritative: UTF-8, LF, final newline, tabs at width 4. YAML, Markdown and
  JSON use spaces.
- C# has nullable reference types and implicit usings enabled. Follow the existing file-scoped
  namespaces, primary constructors, records, and cancellation-token propagation.
- MUI 9 removed system props: layout goes in `sx`, not as direct props on `Stack`/`Typography`.
- This project's namespace ends in `.MongoDB`, which shadows the driver's `MongoDB.*` namespaces.
  Import the nested namespace rather than writing a fully qualified type.
- Comments explain concurrency, security or design constraints — not what the code already says.
- Product UI text is French; code, identifiers and comments are English.

## Deployment cautions

`deploy/build/build.ps1` builds, pushes to an external registry and deploys an external Helm chart.
Run it only when explicitly asked to publish. Run a single replica: the collector and DNS writer are
singletons.
