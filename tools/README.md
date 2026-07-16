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

Package / publish client releases:

```bash
./tools/release.sh                 # host platform (+ linux-amd64), with image
./tools/release.sh --linux-only    # linux-amd64 + linux-arm64
./tools/release.sh --all           # linux/darwin × amd64/arm64
./tools/release.sh --upload        # package then upload (s3cmd / aws / rclone)
./tools/release.sh --upload --verify   # upload then post-release install check
./tools/release.sh --upload --dry-run
```

Stages a clean tree at **`dist/upload/`** (only the objects to publish).  
See `tools/release.sh` for `RELEASE_BUCKET`, `RELEASE_ENDPOINT`, `RELEASE_S3CFG`, `RELEASE_UPLOAD_CMD`, etc.

s3cmd looks for config at `RELEASE_S3CFG`, then `$S3CMD_CONFIG`, then `~/do-tor1.s3cfg`, then `~/.s3cfg`.

### Post-release install check

After a publish, verify the live installer end-to-end inside an isolated Ubuntu container
(downloads `install.sh`, installs the CLI, optionally `placecontext install --docker`):

```bash
./tools/integration_test_install.sh
./tools/integration_test_install.sh --base https://placecontext.syd1.digitaloceanspaces.com
./tools/integration_test_install.sh --cli-only          # skip k3d (fast)
./tools/integration_test_install.sh --keep              # leave container + cluster
```

Prints a PASS/FAIL check table and exits non-zero on failure. Requires host Docker
(and ~4+ GiB free disk for the cluster path).

Low-level single-target package:

```bash
./tools/pctl package [--os linux|darwin] [--arch amd64|arm64] [--no-image]
```

Release layout expected by `deploy/install.sh`:

| Key | Purpose |
|-----|---------|
| `install.sh` | curl \| bash entry |
| `latest/VERSION` | plain version string |
| `latest/placecontext-{os}-{arch}.tar.gz` | client binary + lib assets |
| `{version}/…` | optional pinned releases |

The tarball contains **only** client pieces (not this `tools/` tree).
