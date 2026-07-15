# Internal tooling (not delivered to clients)

| Path | Purpose |
|------|---------|
| `tools/pctl` | Full control plane: **build**, **package**, k3d **dev**, mesh, HA DB, image import, etc. |
| `tools/legacy-tui/` | Previous ops dashboard TUI (cluster/jobs/logs) — superseded by the client operator TUI |

## Client-facing (shipped)

| Path | Purpose |
|------|---------|
| `deploy/placecontext` | Client CLI: **install / upgrade / connect / status / logs / url** |
| `deploy/tui/` | New Bubble Tea TUI for install · upgrade · connect |
| `deploy/k3s/` | Kubernetes manifests |
| `deploy/install.sh` | One-shot installer that puts `placecontext` on PATH |

Package client releases:

```bash
./tools/pctl package                 # → dist/placecontext-{os}-{arch}.tar.gz + dist/latest/…
# upload dist/install.sh, dist/latest/VERSION, dist/latest/*.tar.gz to the release host
```

Release layout expected by `deploy/install.sh`:

| Key | Purpose |
|-----|---------|
| `install.sh` | curl \| bash entry |
| `latest/VERSION` | plain version string |
| `latest/placecontext-{os}-{arch}.tar.gz` | client binary + lib assets |
| `{version}/…` | optional pinned releases |

The tarball contains **only** client pieces (not this `tools/` tree).
