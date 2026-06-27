# PlaceContext deploy — `pctl`

One CLI for the whole cluster lifecycle, on a laptop or across a fleet.

```
deploy/
  install.sh      ← one-command installer: deps + global `placecontext` cmd + first-run setup
  pctl            ← the engine (bash): all orchestration logic, idempotent
  tui/            ← reactive Go TUI dashboard (Bubble Tea), wraps the engine
  k3s/            ← Kubernetes manifests (Postgres + PlaceContext Host + Ingress)
  headscale/      ← self-hosted WireGuard mesh control plane (driven by `pctl mesh`)
  terraform/      ← IaC to provision the Headscale mesh control droplet on DigitalOcean
  selfhost.sh     ← deprecated shim → forwards to `pctl server up` / `pctl agent join`
```

## Install (one command)

```bash
# local dev (k3d on this machine):
./deploy/install.sh

# production server (real k3s, activation enforced):
sudo ./deploy/install.sh --prod --activation-key <KEY>
```

It installs the dependencies it can (k3d, kubectl → `~/.local/bin`), installs a global
**`placecontext`** command (and `pctl`), then on this first run brings the cluster up,
applies your activation key, and configures **autostart on boot** (a systemd *user*
service runs `k3d cluster start` for dev; prod k3s already auto-starts via its own
service). Afterwards just type **`placecontext`** anywhere to open the dashboard.

## Dashboard (TUI)

`placecontext` (or `pctl tui`) opens a reactive full-screen dashboard:

- **3D cluster topology** on the main page — control-plane + workers in a rotating ring,
  pods linked to their node by lines, everything labelled. `←→↑↓` rotate · `+/-` zoom ·
  `space` spin · `v` switch to the interactive list.
- **List view** (`v`) — selectable nodes + pods + jobs; `⏎` logs/detail, `x` kill
  (with an "are you sure?" gate), `a` add node.
- **MCP calls** (`m`) — recent tool calls from `tool_calls`.
- **Portal** (`p`) — opens the portal signed-in. **Brain** (`z`) — 3D dependency view.

## Local dev — a real 1-server + 2-agent cluster on one machine

Uses [k3d](https://k3d.io) (k3s-in-Docker), so you get a genuine multi-node cluster
without VMs. Activation enforcement is **off** for dev.

```bash
./deploy/pctl dev up      # clean dev Docker, create cluster, import image, deploy
./deploy/pctl status      # live nodes + pods
./deploy/pctl logs -f     # tail the Host
./deploy/pctl dev down    # tear the cluster down
```

Portal + MCP: <http://localhost:7700/> (MCP at `/mcp`).

### Reactive dashboard (TUI)

```bash
./deploy/pctl tui         # builds the binary on first run, then launches it
# or build a static binary directly:
make -C deploy/tui            # → deploy/tui/pctl-tui
make -C deploy/tui install    # → ~/.local/bin/pctl-tui
make -C deploy/tui fleet      # cross-compiled static binaries → deploy/tui/dist/
```

Full-screen, auto-refreshing: PlaceContext banner, health line, live node/pod tables,
and keys `[u]`p `[d]`own `[r]`efresh `[l]`ogs `[q]`uit. It shells out to `pctl` for
actions (single source of truth) and polls `kubectl` for the live view.

## Production — k3s across multiple machines

Real k3s with genuine separate nodes. Activation enforcement is **on**.

```bash
# on the server machine (installs a systemd service):
sudo ./deploy/pctl server up --activation-key <KEY> [--image <IMG>]

# it prints the join command — run that on each worker machine:
sudo ./deploy/pctl agent join --server-url https://<server-ip>:6443 --node-token <TOKEN>
```

## Prereqs

`pctl doctor` checks them. Dev needs `docker`, `k3d`, `kubectl` (+ `go` to build the TUI);
prod nodes need `curl` (k3s is installed for you). Put `~/.local/bin` on your `PATH`.

## Config (environment overrides)

| Var | Default | Meaning |
|-----|---------|---------|
| `PCTL_CLUSTER` | `placecontext` | k3d cluster name |
| `PCTL_NAMESPACE` | `placecontext` | Kubernetes namespace |
| `PCTL_PORT` | `7700` | host port → ingress `:80` (dev) |
| `PCTL_AGENTS` | `2` | dev worker nodes |
| `PCTL_IMAGE` | `ghcr.io/bradlnz/placecontext:local` | container image ref |
| `PCTL_IMAGE_TAR` | `deploy/placecontext-local.tar` | image tarball to import |
