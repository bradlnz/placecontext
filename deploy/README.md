# PlaceContext deploy — `pctl`

One CLI for the whole cluster lifecycle, on a laptop or across a fleet.

```
deploy/
  install.sh      ← one-command installer: deps + first-run cluster bring-up
  pctl            ← the engine (bash): all orchestration logic, idempotent
  tui/            ← reactive Go TUI dashboard (Bubble Tea), wraps the engine
  k3s/            ← Kubernetes manifests (Postgres + MinIO + Host + Ingress)
  headscale/      ← self-hosted WireGuard mesh control plane (driven by `pctl mesh`)
  terraform/      ← IaC to provision the Headscale mesh control droplet
```

## Install (one command)

```bash
# local dev (k3d on this machine):
./deploy/install.sh

# production master (real k3s):
sudo ./deploy/install.sh --prod
```

It installs the dependencies it can (Docker, kubectl, k3d → `~/.local/bin`), brings the
cluster up, and configures autostart on boot. Packaged releases carry the container image
as a tarball and a prebuilt TUI — no registry access, no Go, no .NET needed on the node.

## Dashboard (TUI)

`./deploy/pctl tui` opens a reactive full-screen dashboard: cluster topology, live
node/pod/job tables, run history with per-shard artifacts, MCP call log (`m`),
metrics graphs (`g`), search (`s`), logs (`l`), join-a-cluster (`j`), portal (`p`).

```bash
make -C deploy/tui            # → deploy/tui/pctl-tui (static binary)
make -C deploy/tui install    # → ~/.local/bin/pctl-tui
```

## Local dev — a real 1-server + 2-agent cluster on one machine

Uses [k3d](https://k3d.io) (k3s-in-Docker): a genuine multi-node cluster without VMs.

```bash
./deploy/pctl dev up      # clean dev Docker, create cluster, import image, deploy
./deploy/pctl status      # live nodes + pods
./deploy/pctl logs -f     # tail the Host
./deploy/pctl dev down    # tear the cluster down
```

Portal + MCP: <http://localhost:7700/> (MCP at `/mcp`).

## Production — k3s across machines, joined over Tailscale

```bash
# on the master (installs a systemd service; uses your saved Tailscale OAuth client):
sudo ./deploy/pctl server up

# print a one-line join code for new workers:
sudo ./deploy/pctl join-code

# on each worker (any Linux box, any network — nodes mesh over Tailscale):
sudo ./deploy/pctl join <CODE>
```

Nodes connect over [Tailscale](https://tailscale.com) (`pctl ts-oauth` saves the OAuth
client once; every join mints its own key) or a self-hosted
[Headscale](https://headscale.net) mesh (`pctl mesh`). No port-forwarding, no static IPs.

## Releases & upgrades

CI publishes self-contained packages per platform on every `v*` tag
(`.github/workflows/release.yml`): `pctl package` output — engine + TUI + manifests +
the image tarball.

```bash
./deploy/pctl update             # git checkout: fast-forward pull
./deploy/pctl update --deploy    # …and rebuild + roll into the cluster
# packaged (non-git) installs: the same command fetches the latest GitHub release,
# lays it over the install, and (--deploy) rolls the new image in.
./deploy/pctl version            # what's installed
```

## Prereqs

`pctl doctor` checks them. Dev needs `docker`, `k3d`, `kubectl` (+ `go` to build the TUI
from source); prod nodes need only `curl` (k3s is installed for you). Put `~/.local/bin`
on your `PATH`.

## Config (environment overrides)

| Var | Default | Meaning |
|-----|---------|---------|
| `PCTL_CLUSTER` | `placecontext` | k3d cluster name |
| `PCTL_NAMESPACE` | `placecontext` | Kubernetes namespace |
| `PCTL_PORT` | `7700` | host port → ingress `:80` (dev) |
| `PCTL_AGENTS` | `2` | dev worker nodes |
| `PCTL_IMAGE` | `ghcr.io/bradlnz/placecontext:local` | container image ref |
| `PCTL_IMAGE_TAR` | `deploy/placecontext-local.tar` | image tarball to import |
| `PCTL_REPO` | `bradlnz/placecontext` | GitHub repo used for release updates |
