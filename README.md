# Proxmox FQDN Exporter

A .NET application for exporting Fully Qualified Domain Names (FQDNs) and IP addresses of Proxmox containers and virtual machines. The exporter can publish DNS records to Technitium DNS, update a custom FQDN list for Pi-hole, and supports flexible export options.

## Features
- Fetches FQDN and IP addresses from Proxmox containers and VMs
- Exports host lists to console, Technitium DNS, and/or Pi-hole
- Supports custom subnet filtering
- Designed for automation and integration with authoritative DNS providers
- Configurable via `config.json`
- **AOT (Ahead-Of-Time) compilation to native executable for direct deployment on Proxmox host**

## Requirements
- .NET SDK matching the project target framework
- Proxmox host with access to `pct` and `qm` commands
- (Optional) Technitium DNS primary node for zone updates
- (Optional) Pi-hole instance for legacy DNS list updates

## Native Executable (AOT)
This project uses .NET's AOT (Ahead-Of-Time) compilation to produce a native executable. This allows the exporter to run directly on the Proxmox host without requiring a .NET runtime. The deployment scripts and Dockerfile are set up to build and publish a self-contained, native binary for your target platform.

## Configuration
Edit the `config.json` file in the root of the project:

```json
{
  "SubnetsFilter": ["10.0.0.0/8"],
  "FqdnRetentionMinutes": 300,
  "Export": {
    "Console": true,
    "Dns": {
      "Technitium": {
        "BaseUrl": "http://ely-dns-01.elylan:5380",
        "ApiToken": "technitium-api-token",
        "Zone": "elylan",
        "PrimaryNode": "ely-dns-01.elylan",
        "RecordTtlSeconds": 300,
        "RecordExpirySeconds": 18000,
        "CreatePtr": false
      }
    },
    "Pihole": {
      "Id": 104,
      "ListFilePath": "/etc/pihole/hosts/custom.list",
      "ExecutablePath": "/usr/local/bin/pihole",
      "Type": "Container"
    }
  }
}
```

- `SubnetsFilter`: CIDR notation for filtering network interfaces
- `FqdnRetentionMinutes`: how long records stay in the local retention database after they disappear from Proxmox
- `Export.Console`: Output host list to console
- `Export.Dns.Technitium`: Technitium DNS export settings
- `Export.Pihole`: Pi-hole export settings

Technitium export writes A records only to the configured primary node. In a clustered or primary/secondary DNS setup, replication is left to Technitium.

The Technitium provider uses `/api/zones/records/add` with overwrite enabled. When `RecordExpirySeconds` is configured, records that stop being refreshed are automatically removed by Technitium after the expiry window.

## Usage

### Build and Run

```
dotnet build

dotnet run --project Proxmox.Fqdn.Exporter
```

### Native Publish (AOT)
To build a native executable for your target platform (e.g., Linux x64):

```
dotnet publish -c Release -r linux-x64 -p:PublishAot=true --self-contained true -o out
```

The resulting binary in the `out` directory can be copied and run directly on your Proxmox host.

### Docker and Cross-Compilation
A Dockerfile and compose file are provided in the `Deployment/build` directory. If your host supports cross-compilation, you can use Docker Compose to build a native Linux x64 executable (AOT) even from a non-Linux host:

```
cd Deployment/build

docker compose up --build
```

This will produce the native binary in the `out` directory, which can then be deployed to your Proxmox host.

### Deployment Script
A PowerShell script (`run.ps1`) is available for building, copying, and running the exporter on a remote host.

## Project Structure
- `Proxmox.Fqdn.Exporter/` - Main application source code
- `Proxmox.Fqdn.Exporter.Tests/` - Unit tests
- `Deployment/build/` - Docker and deployment scripts

## Extending
- Add new export targets by extending the `Export` configuration and implementing new adapters.
- The codebase uses dependency injection and is modular for easy extension.

## License
See [LICENSE](./LICENSE) for details.
