# PlaceContext deploy

Client-facing install and day-2 ops. **Build / package / mesh tooling is not shipped** — see
[`tools/`](../tools/README.md).

```
deploy/
  install.sh       ← one-command installer (deps + first-run bring-up)
  placecontext     ← client CLI: install | upgrade | connect | status | logs | url
  tui/             ← operator TUI (install · upgrade · connect)
  k3s/             ← Kubernetes manifests
  pctl             ← thin shim → tools/pctl for developers only

tools/             ← NOT delivered to clients
  pctl             ← full engine: build, package, dev, mesh, HA, …
  legacy-tui/      ← old ops dashboard (retired)
```

## Install (customers)

```bash
curl -fsSL https://get.placecontext.ai/install.sh | bash
```

Installs the `placecontext` command and opens the TUI. From there (or CLI):

```bash
placecontext              # default: operator TUI
placecontext install      # Docker (k3d) or system service (k3s)
placecontext upgrade
placecontext connect --code PC1.…
placecontext status
```

The first time you run install on a fresh machine, cluster setup will pull the PlaceContext image
from the remote registry if a local `placecontext-local.tar` image is not already available.
Set `LOCAL_IMAGE_ONLY=1` for offline installs that require the packaged image only.

## Operator TUI

```bash
make -C deploy/tui        # → deploy/tui/placecontext-tui
placecontext              # or: placecontext tui
```

Menu:

1. **Install** → how? Docker (k3d) or system service (k3s)
2. **Upgrade**
3. **Connect** to an existing cluster (join code)
4. **Status**

## Developers / packaging

```bash
./tools/pctl build
./tools/pctl package      # client release tarball only (no tools/pctl inside)
./tools/pctl dev up       # local k3d stack
```

Portal + MCP (after install): <http://localhost:7700/> (MCP at `/mcp`).
