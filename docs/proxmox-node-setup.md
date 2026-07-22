# Proxmox node setup

How to grant Proxmox IP Monitor read access to a Proxmox VE node. The app polls the REST API on
port `8006` over HTTPS with an API token — nothing is installed on the hypervisor and there is no
SSH from the app.

## What the app calls, and the privilege each call needs

| Endpoint | Purpose | Privilege |
|---|---|---|
| `GET /api2/json/version` | auth + TLS + routing probe | any (token valid) |
| `GET /nodes/{node}/network` | the hypervisor's own address | `Sys.Audit` |
| `GET /nodes/{node}/qemu`, `/lxc` | list guests | `VM.Audit` |
| `GET /nodes/{node}/qemu/{id}/agent/network-get-interfaces` | a VM's addresses via the guest agent | `VM.GuestAgent.Audit` |
| `GET /nodes/{node}/lxc/{id}/interfaces` | a container's addresses | `VM.Audit` |

> **PVE 8/9 note.** The privilege that guards the guest-agent call is `VM.GuestAgent.Audit`.
> The old `VM.Monitor` privilege no longer exists (`invalid privilege 'VM.Monitor'`), and
> `PVEAuditor` alone is not enough — it does not grant `VM.GuestAgent.Audit`, so VM addresses
> come back empty.

## Setup (run once, as root on the node)

```bash
# 1. A role granting exactly the three privileges needed
pveum role add IpMonitor -privs "Sys.Audit VM.Audit VM.GuestAgent.Audit"

# 2. An application user in the PVE realm (no password: only the token is used)
pveum user add monitor@pve

# 3. The role over the whole tree, propagating down
pveum acl modify / -user monitor@pve -role IpMonitor

# 4. An API token. --privsep 0 lets the token inherit the user's role.
pveum user token add monitor@pve ip-monitor --privsep 0

# 5. (Optional but explicit) grant the role to the token directly as well
pveum acl modify / -token "monitor@pve!ip-monitor" -role IpMonitor
```

Step 4 prints the token id and secret **once**:

```
full-tokenid   monitor@pve!ip-monitor
value          a5b0ce9a-…            <- copy this now, it is never shown again
```

### Pitfalls

- `--privsep 0` matters: with privilege separation on (`1`) the token starts with no rights and
  every call returns `403`. Either use `--privsep 0`, or keep `1` and add an explicit token ACL
  (step 5).
- The node name in the app must be the **real** node name (`hostname` on the node, e.g. `proxmox`),
  since it builds `/nodes/{name}/…`. A wrong name gives `HTTP 500/595`, not `403`.
- For a cluster, the ACL on `/` covers every node, but add **one entry per node** in the app UI —
  each node has its own API URL.
- VM addresses only appear if `qemu-guest-agent` runs **inside** the VM and the agent is enabled on
  the VM. LXC containers need no agent.

## Verify the token from the node

```bash
TOK="PVEAPIToken=monitor@pve!ip-monitor=<secret>"
B="https://127.0.0.1:8006/api2/json"
curl -s -k -o /dev/null -w "%{http_code}\n" -H "Authorization: $TOK" "$B/version"                # 200
curl -s -k -o /dev/null -w "%{http_code}\n" -H "Authorization: $TOK" "$B/nodes/proxmox/network"  # 200
curl -s -k -o /dev/null -w "%{http_code}\n" -H "Authorization: $TOK" "$B/nodes/proxmox/qemu"      # 200
# and, for a running VM with the agent:
curl -s -k -o /dev/null -w "%{http_code}\n" -H "Authorization: $TOK" \
  "$B/nodes/proxmox/qemu/<vmid>/agent/network-get-interfaces"                                     # 200
```

## Fill in the "Ajouter un nœud" form

| Field | Value |
|---|---|
| Nom affiché | anything (a label), e.g. `ely-px-01` |
| URL de l'API | `https://<node-ip>:8006` |
| Nom du nœud dans Proxmox | the node's real hostname, e.g. `proxmox` |
| Identifiant du jeton | `monitor@pve!ip-monitor` |
| Secret du jeton | the `value` UUID from `token add` |
| Accepter le certificat auto-signé | ✅ (PVE serves a self-signed cert on 8006) |
| Interroger ce nœud | ✅ |

Click **Tester la connexion**, then **Enregistrer**.
