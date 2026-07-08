# Getting started

*Install PlaceContext on your own machine, sign in to the portal, and learn where everything lives.*

## What PlaceContext is

PlaceContext is a self-hosted, multi-tenant **context + jobs** platform. It runs on your own
hardware as a small Kubernetes cluster and gives you:

| Piece | What it does |
|---|---|
| **Portal** (Blazor Server) | The web UI: projects, jobs, data, reports, the knowledge graph |
| **MCP server** (Streamable HTTP at `/mcp`) | Lets AI agents (Claude Code, etc.) work against your projects with real tools |
| **PostgreSQL** (pgvector) | The platform database, plus a private schema per project |
| **MinIO** | Object storage for run reports, charts, artifacts, and DB dumps |
| **Ollama + Gemma** | A local LLM — draws charts, writes report prose, powers the per-project agent. Nothing leaves your machines |
| **Job runner** | Executes your code as sandboxed containers on the cluster |

Everything is orchestrated by one CLI, `pctl`, and one full-screen operator console, `pctl tui`.

## Prerequisites

- **docker** — required. The dev cluster is k3s-in-Docker (k3d).
- **k3d** — required for the local cluster. The packaged installer installs it for you if missing.
- **kubectl** — used by `pctl` for status/logs (bundled expectations; `pctl doctor` checks all of this).
- Go and the .NET SDK are only needed if you build from source.

Check your machine before anything else:

```bash
pctl doctor
```

It verifies docker/k3d/kubectl, and — when a cluster is already up — runs the full go-live
checklist: secrets present, database migrated to the code's level, host replicas ready, and a
sandboxed smoke-test job per runtime.

## Way 1 — the packaged tarball (recommended for a new machine)

Releases are built with `pctl package` into a single self-contained tarball: `pctl`, the prebuilt
TUI, the Kubernetes manifests, and the container image itself (`deploy/placecontext-local.tar`) —
no registry, no source tree, no build step.

```bash
tar xzf placecontext-<version>-linux-amd64.tar.gz
cd placecontext-<version>-linux-amd64
./install.sh
```

`install.sh` checks for docker, installs k3d if it's absent (via the official install script),
then runs `./deploy/pctl dev up`. When it finishes:

```
PlaceContext is running:
  portal   http://localhost:7700/
  console  ./deploy/pctl tui        (dashboard, chat, join codes)
  help     ./deploy/pctl help
```

If this machine should instead *join* an existing cluster as a worker, don't install — run the
TUI and press `[j]`, then paste the master's join code (see the *Cluster and nodes* article).

## Way 2 — from a source checkout

```bash
git clone <repo> && cd <repo>
./deploy/pctl dev up            # add --rebuild to build the image first
```

`pctl dev up` does, in order:

1. Cleans any old dev Docker Postgres containers and volumes.
2. Creates the k3d cluster `placecontext` — **1 server + 2 agent nodes**, with host port
   `7700` mapped to the cluster's ingress (`-p 7700:80@loadbalancer`).
3. Imports the container image (from the tarball if present, otherwise the local Docker image).
4. Applies the manifests: namespace, signing-key secrets, PostgreSQL (pgvector), MinIO,
   the nightly DB-dump CronJob, Ollama + Gemma, and the PlaceContext Host.
5. Waits for the rollout and prints the URL.

Useful knobs (environment overrides):

```bash
PCTL_PORT=8800 pctl dev up      # different host port
PCTL_AGENTS=4 pctl dev up       # more dev worker nodes
```

Related commands:

```bash
pctl ensure               # idempotent bring-up: create-or-start cluster, apply, wait
pctl autostart            # run 'ensure' at boot via a systemd user service
pctl status               # nodes + workloads
pctl logs -f              # tail the Host logs
pctl url                  # print the portal / MCP URL
pctl dev down             # delete the local cluster
```

## First login to the portal

The portal is at **<http://localhost:7700/>** (the MCP endpoint is `/mcp` on the same origin).

The easiest first sign-in is through the TUI: run `pctl tui` and press **`[p]`**. The TUI mints a
short-lived sign-in token from the cluster's portal signing key (the `placecontext-portal`
secret, generated once at deploy time) and opens the portal already authenticated. From there,
manage your team under **Settings → Members**.

## A tour of the left nav

| Item | What you'll find |
|---|---|
| **Overview** | All projects at a glance, with their risk bands |
| **Brain** | The knowledge graph — decisions, context, hotspots built from logged activity |
| **Activity Log** | The ledger: every recorded change with author, rationale, and verification flags |
| **Reports** | Defined reports per project, plus the global **Job data** section — stat tiles, an LLM narrative, and auto-generated charts from recent job runs |
| **Requirements** | The standards agents are held to when they work on your projects |
| **Wiki** | This documentation |
| **MCP Inspector** | A live trace of every MCP tool call — request, response, timing, status |

Each project additionally has its own pages: the work-item board, **Jobs**, **Data** (the
project's own SQL database), and the **Vault** (encrypted secrets).

## The operator console: `pctl tui`

```bash
pctl tui
```

The TUI is the day-to-day cockpit: a live dashboard of nodes, pods, and jobs (refreshed about
every 1.5 s), with one-key actions:

- `↑↓`/`jk` navigate · `⏎` open logs (pods/nodes) or run history (jobs)
- `R` run the selected job · `s` per-job settings · `x` kill a job
- `g` live CPU/memory graphs · `m` MCP call log · `/` search the knowledge graph
- `p` open the portal (signed in) · `a` add a worker node · `u` update + deploy
- `t` encrypted node-to-node chat · `c` cycle theme · `q` quit

Before any cluster exists it shows a welcome screen instead: `[u]` creates a local cluster,
`[j]` joins an existing one with a join code.

## If something doesn't come up

| Symptom | Do this |
|---|---|
| Portal doesn't answer on :7700 | `pctl status` — are the `placecontext` pods Running? `pctl logs -f` for the Host's own errors |
| "cluster not reachable" in the TUI | The cluster is down — `pctl ensure` starts (or creates) it and re-applies everything |
| Cluster broken after a `docker system prune` | `pctl ensure` repairs a missing network; if the loadbalancer container is gone the message will say so — `pctl dev down && pctl dev up` recreates it |
| Red migration banner in the TUI | The database schema is behind the app code — `pctl deploy` (or the TUI's `[u]`) rolls the migration out |
| Not sure anything is healthy | `pctl doctor --go-live` — the full checklist, including per-runtime sandbox smoke tests |

## Where to go next

- **Projects** — the top-level unit everything hangs off.
- **Jobs and artifacts** — the core doctrine: jobs exist to generate artifacts.
- **Cluster and nodes** — grow from one laptop to a fleet.
- **MCP and agents** — connect Claude and let agents do the work.
