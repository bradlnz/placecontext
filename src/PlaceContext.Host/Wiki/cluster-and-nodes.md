# Cluster and nodes

*One master plus worker nodes: k3d on your laptop for dev, k3s across machines for production — and a one-string join code to grow the fleet.*

## The cluster model

PlaceContext always runs as a Kubernetes cluster with a **master (server) node** and **agent
(worker) nodes**:

| Mode | Stack | Created by | Scope |
|---|---|---|---|
| **Local dev** | k3d (k3s-in-Docker) | `pctl dev up` | A real 1-server + 2-agent cluster on one machine |
| **Production** | k3s | `pctl server up` + `pctl agent join` | A fleet across physical machines |

The same manifests deploy either way: Postgres (pgvector), MinIO, the nightly DB-dump CronJob,
Ollama + Gemma, and the PlaceContext Host, all in the `placecontext` namespace. Every `pctl`
command is idempotent — safe to re-run.

## Local dev (k3d)

```bash
pctl dev up                 # create the cluster (1 server + 2 agents), import the image, deploy
pctl dev up --rebuild       # build the container image first
pctl dev down               # delete the cluster
pctl dev clean              # remove old dev Docker Postgres containers + volumes
pctl dev add-node --role agent          # add a dev worker (auto-named pc-agent-N)
pctl dev add-node --role server --name pc-server-1
pctl image import           # (re)import the local image into the cluster
```

Environment overrides: `PCTL_CLUSTER` (name, default `placecontext`), `PCTL_PORT` (host port →
ingress, default `7700`), `PCTL_AGENTS` / `PCTL_SERVERS` (node counts), `PCTL_IMAGE`,
`PCTL_NAMESPACE`.

Two commands keep a dev box healthy:

```bash
pctl ensure        # idempotent bring-up: create-or-start the cluster, apply manifests, wait for rollout
pctl autostart     # run 'ensure' at boot (systemd user service; --disable to remove)
```

`ensure` also repairs common damage — e.g. a `docker prune` that removed the cluster network —
and tells you plainly when only a recreate will help.

## Production (k3s across machines)

On the machine that will be the **master**:

```bash
sudo pctl server up
```

This installs k3s as a systemd service, waits for the control plane, deploys PlaceContext, and
prints the worker-join command. Optional mesh flags let the fleet span networks:

```bash
# Managed Tailscale (mints a short-lived key from an OAuth client):
sudo pctl server up --ts-oauth-client-id ID --ts-oauth-secret SECRET

# Self-hosted mesh (Headscale — see `pctl mesh`):
sudo pctl server up --vpn-control https://headscale.example.com --vpn-authkey KEY
```

On each **worker** machine, the explicit form is:

```bash
sudo pctl agent join --server-url https://<master>:6443 --node-token <token>
```

(`pctl join-cmd` on the master prints this command pre-filled.)

## The easy path: join codes

The join code compresses the server URL and node token into **one string** you can paste anywhere:

**On the master:**

```bash
pctl join-code
# Join code — give this to the new computer:
#
#   PC1.aHR0cHM6Ly8xOTIuMTY4LjEuMTA6NjQ0MyBLMTA6...
```

(The code is `PC1.` + base64url of `https://<ip>:6443 <node-token>`; the Tailscale IP is
preferred when the master is on a tailnet, so the code survives NAT and location changes.)

**On the new computer** — either:

- run `pctl tui` and press **`[j]`** on the welcome screen, paste the code, `⏎` — the machine
  joins as a worker (the TUI self-elevates via passwordless sudo, or hands you the exact sudo
  command to run); or
- from a shell: `sudo pctl join --code 'PC1.…'`

Either way the new machine installs k3s in agent mode and registers with the master. New pods and
job shards start scheduling onto it immediately.

## The TUI dashboard

`pctl tui` is the operator console. With a cluster up, the dashboard shows a **spinning globe**
cluster panel on the left (`space` pauses the spin, `+`/`-` zoom) and, on the right, live tables
of **nodes** (name, role, status, version), **pods** (ready, status, restarts, node), and
**jobs** (artifacts produced, source, concurrency, egress, updated) — refreshed about every
1.5 s. Warnings surface at the top: replicas not ready, crashing pods, database down, and —
in red — a DB schema that's behind the app code (pending migrations).

| Key | Action |
|---|---|
| `↑↓` / `jk` | Move the selection across nodes, pods, and jobs |
| `⏎` | Pod/node → logs/describe; job → run history, then per-run detail |
| `R` | Run the selected job |
| `s` | Per-job settings: egress, post-job actions, timeout |
| `x` | Kill the selected **job** (with confirmation; pods and nodes are read-only in the TUI) |
| `/` | Search the knowledge graph (decisions · context · activity) |
| `g` | Metrics: live CPU (millicores) and memory (MiB) line graphs, sampled every 2 s |
| `m` | The MCP tool-call log; `⏎` opens a call's request/response |
| `p` | Open the portal, signed in |
| `$` | Manage the subscription |
| `a` | **Add a worker** — one key adds a k3d agent node |
| `u` | **Update + deploy** — `git pull --ff-only`, rebuild, import, roll out (creates/starts the cluster if needed) |
| `t` | Encrypted node-to-node chat (see *Chat between nodes*) |
| `c` / `r` / `q` | Cycle theme / refresh / quit |

Before any cluster exists, the welcome screen offers `[u]` create a local cluster and `[j]` join
an existing one.

## Status, logs, and diagnosis

```bash
pctl status            # nodes (wide) + all workloads in the namespace
pctl logs -f           # tail the PlaceContext Host logs
pctl url               # print the portal / MCP URL
pctl doctor            # check docker/k3d/kubectl (k3s noted for prod)
pctl doctor --go-live  # full readiness checklist against the running cluster
```

The go-live checklist verifies: cluster reachable, namespace present, both signing secrets
(`placecontext-portal`, `placecontext-oauth`) exist, the database is reachable **and migrated to
the code's level**, host replicas are ready, and each runtime executes a real sandboxed smoke-test
Job (256Mi/1cpu, no egress).

## Destructive operations

```bash
pctl kill pod <name>          # delete a pod (its controller reschedules it)
pctl kill node <name>         # remove a k3d dev node (also deregisters it from Kubernetes)
pctl kill job <name>          # delete a job AND its entire run history + triggers
```

Each prompts unless `--yes` is passed. The TUI's `[x]` uses the same paths, with its own
confirmation modal.

## Updating a running cluster

```bash
pctl update             # fast-forward the source checkout from git (pull only)
pctl update --deploy    # pull + rebuild the image + import + roll out (starts the dev cluster if needed)
pctl build              # build the image and export deploy/placecontext-local.tar
pctl deploy             # apply manifests to the current kube context and restart the rollout
pctl package            # build the self-contained release tarball in dist/ (--no-image to skip the image)
```

The TUI's `[u]` runs `pctl update --deploy` — the one-key "get current and running" action.

## Database resilience

```bash
pctl db backup-now      # gzipped pg_dumpall of every database → MinIO, right now
pctl db backups         # list held dumps (nightly CronJob at 03:00, retention-pruned, 7 days)
pctl db restore         # restore from the latest dump (or --dump KEY); scales the app down/up around it
pctl db ha              # replicated Postgres (1 primary + 2 replicas) via CloudNativePG
pctl db minio           # port-forward the MinIO console/API to localhost (9001/9000)
```

`db restore` is destructive (drops and recreates the databases) and asks for a typed `yes`
unless `--yes` is given.
