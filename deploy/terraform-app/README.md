# Terraform — PlaceContext public edge (DigitalOcean)

Provisions a single, cheap DigitalOcean droplet that acts as the **public entrypoint** for a
PlaceContext deployment whose real cluster runs **locally** (behind NAT). The droplet joins the same
self-hosted **Headscale/Tailscale mesh** the local cluster is on, terminates **public TLS with Caddy**,
and reverse-proxies portal + MCP traffic to the app on the local cluster over the tailnet.

**The droplet runs no app, no database, and no user jobs.** Everything — the PlaceContext app
(portal + MCP + scheduler), Postgres (pgvector), MinIO, and every user job pod — runs on the **local
k3s cluster** (`KubernetesWorkloadRunner`). The droplet is a stateless edge; it holds no secrets or
data and never touches a Docker socket for job execution.

This is the companion to [`deploy/terraform/`](../terraform/) (which provisions the Headscale mesh
control server) — that module gives you the mesh; this one gives that mesh a public front door.

## Topology

```
   Internet ──443/80──▶ ┌─────────────────────────────┐
                        │  DO droplet  (this module)  │
                        │  • Tailscale  → joins mesh   │
                        │  • Caddy      → public TLS   │
                        └──────────────┬──────────────┘
                                       │  reverse_proxy over the tailnet
                        Headscale / Tailscale mesh   ◀── deploy/terraform/ provisions the control server
                                       │
                                       ▼
                        ┌─────────────────────────────┐
                        │  LOCAL k3s cluster (NAT'd)  │
                        │  • PlaceContext app (7700)   │
                        │  • Postgres (pgvector)       │
                        │  • MinIO (artifacts)         │
                        │  • ALL user job pods run HERE │
                        └─────────────────────────────┘
```

Why an edge instead of an all-in-one droplet? The app selects `KubernetesWorkloadRunner` in-cluster
and runs user jobs as Kubernetes Jobs on the local node — so the compute, the data, and the runtimes
all live locally. The droplet only needs to be small and publicly reachable. A pure Tailscale + Caddy
edge also sidesteps k3s ingress port contention you'd hit trying to co-host Caddy on a cluster node.

## What it creates

| Resource | Purpose |
|----------|---------|
| `digitalocean_droplet` | Ubuntu box; cloud-init installs Docker + Tailscale, joins the mesh, runs Caddy |
| `digitalocean_reserved_ip` (+ assignment) | Stable public IP that survives droplet rebuilds |
| `digitalocean_firewall` | Default-deny inbound; opens only `22`, `80`, `443/tcp` |
| `digitalocean_domain` + `digitalocean_record` | A record `domain → reserved IP` (only when `manage_dns = true`) |
| `digitalocean_ssh_key` | Your admin key(s) for SSH access |

Ports **7700 (app), 5432 (Postgres) and 9000/9001 (MinIO) are never exposed** — those live on the
local cluster and are reached only over the encrypted mesh. The droplet publishes just `80`/`443`.

## Prerequisites

1. **A DigitalOcean API token** — `export DIGITALOCEAN_TOKEN=dop_v1_...` (or set `do_token`).
2. **An SSH public key** — the contents of e.g. `~/.ssh/id_ed25519.pub`.
3. **A running Headscale mesh** — provision it with [`deploy/terraform/`](../terraform/) if you
   haven't. You need:
   - its control-server URL (that module's `domain`, e.g. `https://mesh.example.com`) → `mesh_control_url`
   - a pre-auth key for this node, minted on the mesh server:
     ```bash
     ssh root@<mesh-server> 'cd /opt/placecontext && ./deploy/pctl mesh authkey --tenant <id>'
     ```
     → `mesh_authkey`
4. **The local cluster already joined to that mesh** (`pctl server up` / `pctl join`), and its
   portal reachable over the tailnet. Get the upstream `host:port` from the local node:
   ```bash
   tailscale ip -4          # → the local node's tailnet IP, e.g. 100.64.0.5
   ```
   The ingress serves `:80` on a prod k3s node (k3d dev maps a host port such as `:7700`).
   → `app_upstream` (e.g. `100.64.0.5:80`, no scheme).
5. **(Optional) A domain** for automatic HTTPS — must end up resolving to the reserved IP.

## Usage

```bash
cd deploy/terraform-app
export DIGITALOCEAN_TOKEN=dop_v1_...        # or set do_token in terraform.tfvars
cp terraform.tfvars.example terraform.tfvars
$EDITOR terraform.tfvars                    # ssh_public_keys, mesh_control_url, mesh_authkey, app_upstream

terraform init
terraform plan
terraform apply
```

`terraform output next_steps` prints the post-apply runbook (wait for cloud-init, verify the tailnet
reaches the app, open the portal).

### Reaching the portal

- **With a domain:** `https://<domain>` — Caddy auto-issues a Let's Encrypt cert once the name
  resolves to the reserved IP. WebSocket/SignalR upgrades (Blazor Server circuits) pass through
  Caddy's `reverse_proxy` by default.
- **Without a domain:** `http://<reserved_ip>` (plain HTTP on port 80).

The edge only works once the **local cluster's app is up and reachable over the mesh** at
`app_upstream`. If the portal 502s, check `tailscale status` on the droplet and that the local node's
ingress is listening on the port you set.

## Sizing & cost

`droplet_size` defaults to **`s-2vcpu-2gb` (~$18-20/mo)** to match the project's ~$20 target. As a
pure edge (only Tailscale + Caddy run here — no Docker-in-Docker jobs, Postgres or MinIO), this is
very roomy: you can safely drop to **`s-1vcpu-1gb` (~$6/mo)** or `s-1vcpu-2gb` (~$12/mo) with no
practical impact. Set it in `terraform.tfvars`.

## DNS

- **`manage_dns = true`** (requires `domain` set and its apex hosted in DigitalOcean DNS): the A
  record `domain → reserved_ip` is created for you.
- **`manage_dns = false`** (default): create an A record yourself pointing `domain` at the
  `reserved_ip` output. Caddy can't issue the cert until that record resolves.

## How the mesh join works

Cloud-init installs Tailscale and runs, on first boot:

```bash
tailscale up --login-server <mesh_control_url> --authkey <mesh_authkey> \
             --hostname <mesh_hostname> --accept-routes
```

This is the same self-hosted control server and pre-auth-key model the local cluster nodes use
(`pctl mesh authkey`) — the droplet simply registers as one more node on the tailnet. Caddy then
reverse-proxies public traffic to `app_upstream` across that encrypted mesh. No secrets are generated
or stored on the droplet; the only sensitive input it holds is the mesh pre-auth key (in
`/opt/placecontext-edge/…` via the bootstrap, root-only).

### Security note

Because the droplet is stateless and runs no jobs, it needs **no Docker socket** and executes no
untrusted code — a much smaller blast radius than an all-in-one host. Its only public surface is
Caddy on 80/443; SSH (22) should be tightened with `ssh_allowed_cidrs` for production. Traffic to the
app rides the WireGuard-encrypted mesh, so Postgres/MinIO/the app port are never exposed to the
internet.

## Operating

```bash
ssh root@<reserved_ip>
tailscale status                                                  # mesh membership
docker compose -f /opt/placecontext-edge/docker-compose.yml ps    # Caddy
docker compose -f /opt/placecontext-edge/docker-compose.yml logs -f caddy
```

To change the upstream or domain, edit `terraform.tfvars` and `terraform apply` (the droplet is
re-created with fresh cloud-init), or edit `/opt/placecontext-edge/Caddyfile` on the box and
`docker compose restart caddy` for a quick change.

## Alternative: droplet as a real k3s node

If you'd rather the droplet be an actual cluster node (e.g. `pctl agent join` over the mesh) so k3s
schedules the app/ingress onto it, you can — but then let **Traefik** (k3s's built-in ingress)
terminate TLS instead of a host Caddy, to avoid both fighting for host ports `80`/`443`. Pin user job
pods to the local node (nodeSelector/taint-toleration) so jobs still run locally. This module
deliberately takes the simpler stateless-edge path; the k3s-node path is a larger change to the
existing `deploy/k3s/` manifests.

## Teardown

```bash
terraform destroy
```

State (`*.tfstate`) and `terraform.tfvars` are gitignored — they can contain the API token and the
mesh pre-auth key. Use a remote backend for team/shared use. After destroy, remove the droplet's node
from Headscale if it lingers (`pctl mesh` / `headscale nodes list|delete` on the mesh server).
