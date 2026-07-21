# PlaceContext — Full Setup Guide

How to stand up PlaceContext — locally or across a fleet — configure its features, and operate it.
Everything is driven by one CLI, **`deploy/pctl`** (and its TUI, `pctl tui`).

The fastest path is the guided wizard, which walks every step in this guide interactively:

```bash
./deploy/pctl setup
```

---

## 1. What you get

| Piece | What it is |
|------|------------|
| **PlaceContext Host** | Portal (Blazor) + MCP server (Streamable HTTP at `/mcp`) + schedulers, 2 replicas |
| **PostgreSQL (pgvector)** | The platform store — projects, ledger, jobs, charts, embeddings (RAG) |
| **MinIO** | Object store for run artifacts (reports/charts/CSVs) + nightly DB dumps |
| **Docker-in-Docker** | The per-project application runtime (portal **Runtime** tab) |
| **k3d / k3s** | The cluster: k3d for one-machine dev, k3s for a real fleet |

---

## 2. Prerequisites

| Need | For |
|------|-----|
| Docker | the local dev cluster (k3d) and image builds — on macOS via Docker Desktop or colima |
| `curl` | installers + Tailscale OAuth |
| openssl | generating signing keys (portal/OAuth) |
| Go (1.26+) | building the TUI (only if you run it from source) |
| A [Tailscale](https://tailscale.com) account | fleets — **nodes connect via Tailscale** |

`pctl doctor` checks the tooling. Put `~/.local/bin` on your `PATH` (the installer puts `k3d`,
`kubectl`, `pctl`, and `placecontext` there).

---

## 3. Guided setup (recommended)

```bash
./deploy/pctl setup
```

The wizard walks through, in order:

1. **Doctor** — verify/install tooling.
2. **Mode** — dev cluster on this machine, production master, or join an existing cluster.
3. **Tailscale OAuth** (fleet modes) — save the OAuth client once (`pctl ts-oauth`); every
   join afterwards mints its own tailnet key.
4. **Platform keys** — generate the event-ingest key (or skip); they're
   stored in a local overlay and re-applied on every deploy.
5. **Bring-up** — `dev up`, `server up`, or `join --code …` for the chosen mode.
6. **Next steps** — portal URL, MCP endpoint, and the first-project checklist.

Everything the wizard does maps to the plain commands below, so unattended installs can script
the same steps.

## 4. One-command install (alternative)

```bash
./deploy/install.sh              # local dev (k3d on this machine)
sudo ./deploy/install.sh --prod  # production server (real k3s + systemd)
```

Installs dependencies and the global **`placecontext`** command, brings the cluster up, and (dev)
configures autostart. Then just type `placecontext` to open the dashboard.

---

## 5. Local dev cluster (k3d)

A real 1-server + 2-agent k3s cluster in Docker — multi-node without VMs.

```bash
./deploy/pctl build         # container image → deploy/placecontext-local.tar
./deploy/pctl dev up        # clean dev docker, create cluster, import image, deploy
./deploy/pctl status        # nodes + pods + jobs
./deploy/pctl dev add-node --role agent
./deploy/pctl dev down      # tear down
```

Portal + MCP: <http://localhost:7700/>. First sign-in creates the tenant.

Hacking on PlaceContext itself? `./run.sh` runs the Host directly against a Docker Postgres —
but jobs, the DinD runtime, and MinIO features need the cluster.

---

## 6. Production fleet — nodes connect via Tailscale

Nodes mesh over Tailscale using k3s's native `--vpn-auth`; an **OAuth client** drives it so nobody
handles keys.

1. In the Tailscale admin console create an OAuth client with the `auth_keys` write scope and a
   device tag (default `tag:k8s` — define the tag in your ACLs first).
2. Save it once on the master:

   ```bash
   sudo ./deploy/pctl ts-oauth --client-id <id> --secret <secret>
   ```

   (Root saves to `/etc/placecontext/ts-oauth.env`, non-root to `~/.config/placecontext/`,
   mode 600. `--status` / `--clear` manage it.)

3. Bring up the master — a fresh tailnet key is minted automatically:

   ```bash
   sudo ./deploy/pctl server up
   ```

4. Add a worker — on the master:

   ```bash
   sudo ./deploy/pctl join-code
   ```

   prints a one-string `PC2.` code carrying the master's **tailnet** address, the k3s node token,
   and a freshly minted single-use Tailscale key (valid 1 hour; the node itself is a durable
   tailnet device). On the new machine:

   ```bash
   sudo ./deploy/pctl join --code 'PC2.…'
   ```

   One string → on the tailnet → in the cluster. The TUI's `[j]` screen does the same. A machine
   with its own saved `ts-oauth` client can join even from a keyless code.

### Self-hosted mesh (Headscale) instead

```bash
sudo ./deploy/pctl mesh up --domain mesh.example.com   # your own control server
./deploy/pctl mesh tenant add <customer>               # isolated private network per customer
./deploy/pctl mesh authkey --tenant <customer>         # persistent key for cluster nodes
sudo ./deploy/pctl server up --vpn-control https://mesh.example.com:443 --vpn-authkey <KEY>
```

Provision the control-server droplet (reserved IP, firewall, DNS, cloud-init) with Terraform —
see [`deploy/terraform/`](../deploy/terraform/README.md).

---

## 7. Platform features (configuration)

All optional; each unlocks a feature. Settings are environment on the `placecontext` deployment
(`PlaceContext__…` double-underscore form). `pctl setup` generates the two keys and stores them in
a local overlay (`/etc/placecontext/platform-keys.env` as root, else
`~/.config/placecontext/platform-keys.env`) that **every `pctl deploy` re-applies** — redeploys
never lose them.

| Feature | Setting | Notes |
|---|---|---|
| **Event ingest webhook** | `PlaceContext:Ingest:Key` | External systems `POST /ingest/{event}` with `X-Ingest-Key`; disabled until set |
| **GitHub import** | `PlaceContext:GitHub:ClientId` / `ClientSecret` | OAuth app; callback `{host}/auth/github/callback` |
| **LLM provider** | `PlaceContext:Llm:Provider` | `none` (default — the jobs pipeline is deterministic) or `anthropic` + `ApiKey` for report polish |
| **App runtime** | `PlaceContext:Runtime:DockerEndpoint` / `AppHost` | Pre-wired to the bundled DinD in the manifests |
| **Warm images (Docker runner)** | `PlaceContext:WorkloadRunner:WarmImages` | Default `true`; see below |
| **Warm dep cache (K8s runner)** | `PlaceContext:WorkloadRunner:WarmDependencyCache` | Default `true`; needs the object store |

### Warm dependency layers (jobs)

A code workload that ships its runtime's dependency manifest (`requirements.txt`, `package.json`,
`Gemfile`, `go.mod`) no longer installs packages on every shard of every run. The layer is keyed
by a hash of the runtime + base image + manifest/lockfile contents — change a dependency and a
new layer is baked; change only code and the warm layer is reused.

- **Local (Docker runner):** the first run builds a `pcwarm-<runtime>:<hash>` image (base image +
  the package install baked in); later runs start plain containers from it. Prune with
  `docker image prune --filter label=placecontext.warm=true`.
- **In-cluster (Kubernetes runner):** a one-shot bake Job tars the installed deps and uploads them
  to the `placecontext-deps` MinIO bucket; shard pods fetch + extract the tar in their init step
  and skip the install. Warmed pods get scoped egress (MinIO + DNS only) instead of the usual
  deny-all. Clear the cache by deleting the bucket's contents.

Every warm path falls back to the per-run install on any failure — a run never fails because
warming did. Bake-time package downloads happen on the host / bake pod; job code still runs
`--network none` unless the job opts into egress.

---

## 8. First project

1. **Portal → Onboarding**, or import: **GitHub** (settings → GitHub) or an **Obsidian vault**
   (`/import` → upload the vault .zip — notes and `[[wikilinks]]` become the project graph).
2. **Connect an agent over MCP** at `http://<host>:7700/mcp`; the `onboard` tool bootstraps a
   repo in one call.
3. **Install the hermes skill** — call the `setup_hermes` MCP tool with the project id; it writes
   `.claude/skills/hermes/SKILL.md`, the job-orchestration playbook (author → upload → run →
   monitor → automate).

Around the platform: **Jobs** (sandboxed map/reduce + artifacts + triggers), **Data** (each
project's own SQL database), **Analytics** (background LLM-drawn charts), **Runtime** (the
project's DinD containers, with browser access via the authenticated
`/runtime/{project}/{port}/` proxy — label containers `placecontext.project=<project-guid>`),
**Vault** (encrypted secrets), **Events** (types, log, inbound SMS, triggers), and the
**notifications bell** (all long-running work runs in the background).

---

## 9. The dashboard (TUI)

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
| `j` | join this computer to a cluster with a join code |
| `$` | open the subscription/billing portal |
| `x` | kill the selected pod / node / job (confirmed) |
| `a` | add a node |
| `i` | about |
| `c` | cycle color theme |
| `q` | quit |

---

## 10. Database: replication + nightly dumps

```bash
./deploy/pctl db ha            # CloudNativePG: 1 primary + 2 replicas
./deploy/pctl db backup-now    # dump all DBs to MinIO right now (nightly CronJob runs at 03:00)
./deploy/pctl db backups       # list the dumps currently held (7-day retention)
./deploy/pctl db restore       # restore from the latest dump (or --dump KEY)
./deploy/pctl db minio         # browse the store at http://localhost:9001
```

> **Backups are bounded by design.** Nightly gzipped `pg_dumpall`, pruned after 7 days
> (`RETENTION_DAYS` in `deploy/k3s/pg-backup.yaml`). You restore to the nightly snapshot, not to
> an arbitrary instant.

---

## 11. pctl environment

| Var | Purpose |
|-----|---------|
| `PCTL_CLUSTER` / `PCTL_NAMESPACE` / `PCTL_PORT` | dev cluster name / namespace / host port |
| `PCTL_IMAGE` | container image ref |
| `PCTL_TS_OAUTH_ID` / `PCTL_TS_OAUTH_SECRET` | Tailscale OAuth client (overrides the saved one) |
| `PCTL_BILLING_URL` | subscription portal the TUI opens on `$` |
| `PCTL_KEY` | the customer's PlaceContext subscription key (TUI) |
| `PCTL_MESH_CONTROL` / `PCTL_MESH_EXCHANGE` | mesh control-server URL / subscription→mesh-key endpoint |

---

## 12. Day-2

```bash
pctl status            # nodes + workloads
pctl logs -f           # tail the Host
pctl update --deploy   # pull latest source, rebuild, roll out
pctl autostart         # bring the stack up on boot
pctl package           # self-contained release tarball (installer, image, TUI, manifests)
```

---

## Local Ollama Setup (Chat Agent)

The chat agent connects to a local Ollama instance for SLM inference. In-cluster, Ollama is
already deployed via `deploy/k3s/ollama.yaml`. For local development:

```bash
# Install Ollama (macOS / Linux)
curl -fsSL https://ollama.ai/install.sh | sh

# Pull the default model (qwen3.5:0.8b)
ollama pull qwen3.5:0.8b

# Ollama runs on http://localhost:11434 by default
```

Set the configuration in `appsettings.json` (or via environment variables):

```json
"PlaceContext": {
  "Chat": {
    "Endpoint": "http://localhost:11434",
    "Model": "qwen3.5:0.8b"
  }
}
```

When `PlaceContext:Chat:Endpoint` is unset or empty, the chat agent gracefully degrades with
a "no model configured" message. No external API key is required after the model is pulled.

---

*PlaceContext — a context platform for AI. Built by Bradley Lietz of CTRL SIGNAL SOFTWARE PTY LTD.*
