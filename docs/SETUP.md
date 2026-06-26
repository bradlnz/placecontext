# PlaceContext — Setup & Operations

How to stand up PlaceContext (locally or across a fleet), run the dashboard, and operate the
database and mesh. Everything is driven by one CLI, **`deploy/pctl`** (and its TUI).

---

## 1. What you get

- **PlaceContext Host** — the portal (Blazor) + MCP server (Streamable HTTP at `/mcp`) + job scheduler.
- **PostgreSQL (pgvector)** — project data, decisions, context, embeddings (RAG).
- **`pctl`** — a CLI that manages the whole lifecycle; **`pctl tui`** is a live dashboard.
- Optional: **replicated Postgres + PITR backups**, a **WireGuard mesh** for multi-location fleets.

---

## 2. Prerequisites

| Need | For |
|------|-----|
| Docker | the local dev cluster (k3d) |
| Go (1.26+) | building the TUI (only if you run it from source) |
| `curl` | installers |
| openssl | generating signing keys (portal/OAuth) |

`pctl doctor` checks the tooling. Put `~/.local/bin` on your `PATH` (the installer puts `k3d`,
`kubectl`, `pctl`, and `placecontext` there).

---

## 3. One-command install

```bash
# local dev (k3d on this machine):
./deploy/install.sh

# production server (real k3s + systemd, on the box that will host it):
sudo ./deploy/install.sh --prod
```

It installs dependencies, installs the global **`placecontext`** command, brings the cluster up,
and (dev) configures autostart on boot. Then just type `placecontext` to open the dashboard.

---

## 4. Local dev cluster (k3d)

A real 1-server + 2-agent k3s cluster in Docker — multi-node without VMs.

```bash
./deploy/pctl dev up        # clean dev docker, create cluster, import image, deploy
./deploy/pctl status        # nodes + pods + jobs
./deploy/pctl dev add-node --role agent
./deploy/pctl dev down      # tear down
```

Portal + MCP: <http://localhost:7700/>.

---

## 5. The dashboard (TUI)

```bash
placecontext          # or: ./deploy/pctl tui
```

| Key | Action |
|-----|--------|
| `↑↓` | navigate the node/pod/job list (right pane) |
| `⏎` | logs (pod) · detail (node) · runs+output (job) |
| `/` | search decisions / context / activity (renders as markdown) |
| `g` | metrics — CPU + memory line graphs across every node |
| `m` | MCP calls (⏎ drills into request/response) |
| `p` | open the portal (auto signed-in) |
| `$` | open the subscription/billing portal |
| `x` | kill the selected pod / node / job (confirmed) |
| `a` | add a node |
| `c` | cycle color theme |
| `q` | quit |

The left pane is a live cluster view: the control-plane node(s) as a rotating ASCII planet, workers
and pods orbiting as satellites, with pulses on the app→DB links.

---

## 6. Production fleet (k3s across machines)

```bash
# on the server machine:
sudo ./deploy/pctl server up
# it prints the worker-join command:
sudo ./deploy/pctl agent join --server-url https://<server-ip>:6443 --node-token <TOKEN>
```

### Multi-location mesh (WireGuard)

Two options, same `--vpn-*` plumbing:

- **Managed Tailscale**: `server up`/`agent join --ts-oauth-client-id ID --ts-oauth-secret SECRET`.
- **Self-hosted (your own control plane)** — run Headscale on a droplet and gate access via PlaceContext:
  ```bash
  sudo ./deploy/pctl mesh up --domain mesh.example.com   # start the control server
  ./deploy/pctl mesh tenant add <customer>               # isolated private network per customer
  ./deploy/pctl mesh authkey --tenant <customer>         # persistent key for cluster nodes
  sudo ./deploy/pctl server up --vpn-control https://mesh.example.com:443 --vpn-authkey <KEY>
  ```
  TUI viewers join ephemerally (auto-removed on close); the cluster stays connected so data syncs.

---

## 7. Database: replication + point-in-time recovery

```bash
./deploy/pctl db ha            # CloudNativePG: 1 primary + 2 replicas + continuous backups (MinIO)
./deploy/pctl db backup-now    # on-demand base backup
./deploy/pctl db minio         # browse the backup store at http://localhost:9001
./deploy/pctl db restore --time "2026-06-26 01:38:00+00" [--cutover]   # PITR into a new cluster
```

App reads/writes go to `placecontext-pg-rw` (primary); read-only scale-out via `placecontext-pg-ro`.

---

## 8. Configuration (environment)

| Var | Purpose |
|-----|---------|
| `PCTL_CLUSTER` / `PCTL_NAMESPACE` / `PCTL_PORT` | dev cluster name / namespace / host port |
| `PCTL_IMAGE` | container image ref |
| `PCTL_BILLING_URL` | subscription portal the TUI opens on `$` |
| `PCTL_KEY` | the customer's PlaceContext subscription key (TUI) |
| `PCTL_MESH_CONTROL` / `PCTL_MESH_EXCHANGE` | mesh control-server URL / subscription→mesh-key endpoint |

---

## 9. Day-2

- `pctl status` / `pctl logs -f` — health and logs.
- `pctl build` → `pctl image import` → `kubectl rollout restart deploy/placecontext` — ship a Host change.
- `pctl autostart` — start the dev cluster on boot.
