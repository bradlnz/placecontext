# PlaceContext — Jobs Automation Progress

Branch: `main` · all jobs-automation work merged
Last updated: 2026-06-25

Status: **212 tests passing** (4 Docker-gated skipped), all layers build clean.

This document tracks the jobs-automation work: scheduled/event triggers, an events layer,
run parameters, run artifacts, a configurable LLM organize step, and the local installer —
plus the roadmap items still to do.

---

## ✅ Done (committed on this branch)

| # | Item | Notes |
|---|------|-------|
| 1 | **Job triggers domain** | `JobTrigger` (cron Schedule / Event subscribe), `EventDefinition`, `EventOccurrence`, `TriggerKind`/`EventSource`, `BuiltInEvents`. |
| 2 | **Persistence** | Tenant-owned rows + EF repos + query filters; migration `AddJobTriggersAndEvents` (`job_triggers`, `event_definitions`, `event_occurrences`). |
| 3 | **Application** | Create/enable/delete/list trigger + define/emit/list event commands & queries; `EventDispatchService` (fan-out → enqueue a run per subscribed trigger); `ScheduleScanService` (fires due schedules). Built-in `job.completed` / `activity.recorded` events raised. Ports `ICronSchedule`, `IJobRunQueue`. |
| 4 | **Infrastructure** | `CronosCronSchedule` (5/6-field, tenant tz), in-process `InMemoryJobRunQueue`, `TriggerSchedulerService` (BackgroundService: scans schedules across tenants + drains the run queue, re-establishing ambient tenant). |
| 5 | **MCP tools** | `create_trigger`, `list_triggers`, `set_trigger_enabled`, `delete_trigger`, `define_event_type`, `emit_event`, `list_event_types`, `list_event_occurrences`. |
| 6 | **UI** | Triggers panel on the Jobs page; Events page + project nav tab. |
| 7 | **Run-input parameters + modal** | `JobParameter` VO + `Job.Parameters`/`RequiresInput`; `RunJobCommand.InputPayload` override (single shard); editor field + run modal; `run_job` MCP gains `inputPayload`; migration `AddJobParameters`. |
| 8 | **Run artifacts** | `RunArtifact` VO; `DockerWorkloadRunner` captures all `/out` files except `result.json` (≤5 MB, ≤50 files); surfaced per shard/reduce in the run detail + `get_job_run`, downloadable in the UI via data-URI links. |
| 14 | **Configurable LLM gateway** | `PlaceContext:Llm:Provider = none\|anthropic\|ollama`; added `OllamaLlmGateway` (local Gemma e.g. `gemma3:4b`) beside Anthropic/Null. `RunJobHandler` **organizes** each run's output through the gateway before storing it to project context (best-effort, falls back to raw). |
| 15 | **`setup.sh` installer** | One-shot idempotent bootstrap: .NET 10 SDK, Docker check, `placecontext-db` Postgres, dotnet tools, EF migrations, optional `--with-ollama` (Ollama + Gemma). Hands off to `./run.sh`. |
| 12a | **Activation-key licensing** | Offline-verifiable signed token (ECDSA P-256/SHA-256 DER, BCL-only). `IActivationService` + `SignedActivationService` (verifies against the committed public key, checks expiry). Host: startup log + `/mcp` enforcement (portal stays visible) + portal banner. `tools/activation.sh` keygen/sign (licensor private key gitignored). Interop proven: openssl-minted key validates in .NET. |
| 12c | **Online activation service** | Phone-home licensing. `RemoteActivationService` (selected when `PlaceContext:Activation:ServerUrl` is set) refreshes entitlement on a timer (`ActivationRefreshService`), follows signing-key **rotation** via an endorsement chain anchored on the configured public key, and **caches last-good through a grace window** (`GraceDays`, default 7) when the server is unreachable — while honouring explicit denials (402 past-due / 403 revoked) immediately. Verification refactored into a shared `ActivationTokenVerifier` (multi-key) reused by the offline path. New standalone **`PlaceContext.Licensing`** minimal-API server (the licensor side): `/activate`, `/keys`, `/admin/rotate`, `/health`; `ActivationSigner` (sign + rotate), config-seeded `LicenseStore`, and a pluggable `IPaymentProvider` (`StubPaymentProvider`, Stripe-ready). Client + server unit-tested (grace, rotation-chain trust, denial, signer interop). |
| 12b | **Self-host on k3s** | `Dockerfile` (Host image); `deploy/k3s/` (Postgres **pgvector** + Host Deployment/Service/Ingress, activation enforced via secret); `deploy/selfhost.sh --activation-key <KEY>` installs k3s, deploys, and prints the worker-node join command. Fixed `UseUrls` to honor `ASPNETCORE_URLS` (was loopback-only → unreachable in a container). |
| 11 | **External event ingress** | `POST /ingest/{eventName}` — an external source (form/webhook/Cloudflare Queue consumer) emits an event into the tenant (resolved by subdomain), firing subscribed event-triggers with the JSON body injected as the runs' input payload. Gated by `PlaceContext:Ingest:Key` (constant-time check; disabled when unset); activation-enforced. **Live-verified** (401 no/wrong key, 200 + occurrence with key). |
| 10 | **Scheduler scales on k3s** | Durable DB-backed run queue (`pending_job_runs`): producers enqueue transactionally; the background scheduler drains with `FOR UPDATE SKIP LOCKED` so **any replica** executes runs without duplication, and runs survive restarts. Schedule-scan is guarded by a **Postgres advisory lock** (one replica scans) — environment-agnostic leader election, no k8s RBAC. Replaces the in-process channel. Deploy bumped to 2 replicas. **Live-verified**: queue row claimed + drained + deleted in ~1s; advisory scan clean. *(The k8s-Job workload runner — shards as k8s Jobs instead of Docker — remains a follow-up.)* |
| 9 | **Voyage embeddings → pgvector** | `IEmbeddingGateway` + `VoyageEmbeddingGateway` + Null fallback (keyed by `PlaceContext:Voyage:ApiKey`). `RunJobHandler` embeds the organized run output and stores it. `EfRunEmbeddingRepository` is **pgvector-backed with lazy self-init** (creates the `vector` extension + `job_run_embeddings` table on first use) and **degrades gracefully** if pgvector/Voyage are absent — so it never touches the migration path or breaks the existing `postgres:16` dev DB. `SearchRunOutputsQuery` + `search_run_outputs` MCP tool do cosine semantic search. New dev containers use `pgvector/pgvector:pg16`. Cosine search unit-tested in-memory; the pgvector path is integration-only (needs a Voyage key + pgvector). |

Commits: `6411749` triggers/events layer → `aec4860` MCP → `1450683` UI → `1240f6a`
parameters/modal → `6c29f00` artifacts → `4ae1900` configurable LLM → `81a4fbb` setup.sh.

---

## ⏳ Remaining (tracked, with decisions)

**#13 reorg landed (folders); namespace-per-slice deferred.** #9–#12 are done and merged into
`main` (see the table above); their original decision notes are retained below for reference.

### #9 — Vectorize run output → dependency graph  *(done)*
Pipeline: job completes → (configurable LLM) **organizes** output → **Voyage AI** embeds it →
stored in the **dependency graph** as queryable nodes.
- **Decision made:** use **pgvector** in the existing Postgres.
- Recon: the graph is assembled **on-read** from activity log + decisions + tool calls
  (`DecisionTreeAssembler`); only a `GraphSnapshotRef` is persisted (`projects.GraphJson`).
- **Built:** `IEmbeddingGateway` port + `VoyageEmbeddingGateway` + Null fallback (provider
  pattern, keyed by `PlaceContext:Voyage:ApiKey`); the `vector` extension (DB image switched to
  `pgvector/pgvector:pg16`); a tenant-owned `job_run_embeddings` table (text + `vector(N)` column);
  `EfRunEmbeddingRepository` with cosine nearest-neighbour search; embed organized run output in
  `RunJobHandler`; a `search_run_outputs` query + MCP tool.
- **Run outputs woven into the graph as the "brain":** `TreeNodeKind.JobRunOutput` + the
  `RunOutputNode` VO; `DecisionTreeAssembler` adds one node per embedded run output and **cross-links
  the semantically-nearest peers** (cosine ≥ 0.6, top-3 each, Inferred edges) so accumulated outputs
  link the dependency graph together into queryable memory. `IRunEmbeddingRepository.ListForProjectAsync`
  (vectors included) feeds them in via `DecisionTreeProvider`; `DecisionTree.Answer` gained a
  brain/memory vocabulary branch. Each per-project graph (run-output nodes included) still rolls up
  into the org-wide `BrainHandler`.

### #10 — Deploy on k3s  *(done)*
A Kubernetes-Job-based `IWorkloadRunner` (shards run as k8s Jobs); the in-process trigger scheduler
needs **leader-election** (or run as a singleton Deployment) so schedules fire once across replicas.

### #11 — External event sources + parameter injection  *(done)*
Webhook ingress + an external queue listener (e.g. **Cloudflare Queue**) that push events in; event
types carry **field definitions**; a fired event injects its payload fields as run **parameters**.
Plumbing started: `QueuedJobRun.Payload` + `RunJobCommand.InputPayload` already flow a payload through.

### #12 — Self-host CLI (changed from Terraform), gated by an activation code  *(done)*
A CLI customers run to self-host: pulls the published image, stands up k3s, applies config/migrations,
and gates usage by an **activation code** (validate against a licensing service or signed offline token;
enforce at startup; surface activation state in the portal).

### #13 — Folder reorganization for human maintainability  *(folders done; namespaces deferred)*
**Decided:** by **feature/vertical-slice** grouping (`Application/Jobs/`, `/Triggers/`, `/Events/`,
`/Reports/`, `/Projects/`, `/Risk/` — command+handler+query+DTO+mapper per slice). Preserve
one-class-per-file + Onion boundaries; update namespaces. **Do AFTER #9–12** to avoid churn.

**Done:** dissolved the flat `Application/Features/` (~130 files) and `Application/Dtos/` (~49 files)
dumps into 21 vertical-slice folders — `Jobs/`, `Triggers/`, `Events/`, `Reports/`, `Projects/`,
`WorkItems/`, `Risk/`, `Graph/`, `Context/`, `Decisions/`, `Requirements/`, `Activity/`, `Cost/`,
`Search/`, `Skills/`, `Onboarding/`, `Focus/`, `Improvements/`, `Organization/` (org-wide rollups),
`Membership/`, plus `Shared/` (`ViewMapper`). Each slice now co-locates its commands, handlers,
queries, DTOs, and mappers. `Cqrs/` (mediator plumbing) and `Ports/` (cross-cutting contracts) were
left intact. All 178 moves are pure `git mv` renames — no content edits.

**Deferred — namespace-per-slice (`PlaceContext.Application.Jobs`, etc.):** the file namespaces are
deliberately left as `…Application.Features` / `…Application.Dtos`, so the slice folders and
namespaces don't yet match. Renaming namespaces means rewriting ~167 `using` statements across
Host/Infrastructure/tests, and a single consumer often pulls types from several would-be slices —
unsafe to do blind because the **.NET SDK download host is blocked by the egress policy here**, so a
missed `using` can't be compiler-caught. Do this pass once a compiler is available to verify the build.

---

## Notes
- Multi-tenancy: every new row is `ITenantOwned` with an EF global query filter; the background
  scheduler sets the ambient `CurrentTenant` (AsyncLocal) per unit of work before dispatching.
- Schema is **EF migrations** (not `EnsureCreated`) — never drop the dev DB; add a migration.
- Convention: **one top-level type per file**.

---

## ✅ Platform / ops / TUI (2026-06-26)

Hosted multi-tenant control plane, deploy CLI + reactive TUI, HA database, mesh, secrets vault,
in-cluster job execution. All committed on `main`; Go TUI builds + tests pass; Host builds clean.

**Deploy CLI + TUI (`deploy/`)**
- `pctl` (bash engine) — one tool for the whole lifecycle: `dev up/down/clean/add-node`, `build`,
  `image import`, `deploy`, `status`, `logs`, `kill pod|node|job`, `autostart`, `tui`, `doctor`,
  prod `server up`/`agent join`. `deploy/install.sh` one-command install + global `placecontext`.
- `deploy/tui/` — Go + Bubble Tea reactive dashboard (modular: `main.go`, `cluster.go`, `metrics.go`,
  `portal.go`, `themes.go`). Side-by-side **cluster (left)** / **node·pod·job list (right)**; the
  cluster is a rotating ASCII **planet** hub with workers/pods as satellites, dotted links + app→db
  pulses. Keys: `↑↓` nav, `⏎` logs/detail, `/` search, `g` metrics, `m` mcp, `p` portal, `$`
  subscribe, `a` add worker, `R` run job (enqueues into the durable run queue), `s` per-job settings
  (checkbox view; toggles allow-network-egress), `x` kill (jobs only — pods/nodes read-only), `c`
  theme (also swaps the banner **font**), `l` global logs.
  Metrics = area charts across every node; search = decisions/context/activity + MinIO files, rendered
  as markdown (glamour); MCP/job drill-down; loading box; first-run setup guide; top alert lines.
  Async fetches off the UI thread; ~1.5s realtime refresh.

**Multi-replica correctness (all fixed + deployed)**
- Shared **Data Protection** keys (DB-persisted) → portal cookie works across replicas.
- Shared **OAuth/MCP RSA signing key** (from a secret, deterministic kid) → MCP tokens validate on any replica.
- **MCP stateless** Streamable-HTTP transport → reconnects don't 404.
- **Traefik sticky sessions** → Blazor Server circuit (SignalR) works (fixed unclickable projects card).

**Database HA + nightly dumps** — `pctl db ha`: CloudNativePG operator + a 3-instance pgvector
cluster (1 primary + 2 replicas, anti-affinity). Backups are **bounded nightly dumps**: a CronJob
(`deploy/k3s/pg-backup.yaml`) `pg_dumpall`s every database to MinIO at 03:00 with 7-day retention;
`db backup-now` / `db backups` / `db restore [--dump KEY]`. Custom CNPG pgvector image
`deploy/postgres/Dockerfile.cnpg`.
*(The previous continuous WAL archiving + PITR was removed: it grew unbounded and filled the host
disk. Re-add PITR only against capacity-managed real S3.)*

**Mesh (multi-location fleet)** — `pctl server up`/`agent join` accept managed-Tailscale OAuth
(`--ts-oauth-*`) **or** self-hosted Headscale (`--vpn-control`/`--vpn-authkey`) via k3s `--vpn-auth`.
Headscale stack in `deploy/headscale/` is **multi-tenant**: per-customer isolated network (tag + default-deny
ACL regenerated from a tenant registry). Mesh access is **gated by PlaceContext** (TUI exchanges the
subscription key for a short-lived mesh key); cluster nodes get persistent keys, TUI viewers ephemeral.

**Jobs run as Kubernetes Jobs in-cluster** — `KubernetesWorkloadRunner` (selected when the Host runs
in-cluster): code+payload via ConfigMap → /work, runtime image pipes input on stdin, stdout→artifact,
deny-egress NetworkPolicy unless opted in, TTL cleanup. ServiceAccount + RBAC in the manifest. Docker
runner stays for local dev. MCP `job_authoring_guide` tool returns the code-structuring contract.

**Encrypted secrets vault** — `job_secrets` table, values encrypted at rest via Data Protection (AES);
Add/Delete/List CQRS + `IPlaceContextService`; secrets immutable (delete+recreate to rotate); decrypted
at run time and injected as env into each k8s Job (never persisted to the run snapshot). Access reuses
the signed-token auth.

**Activation removed** — product no longer key-gated; subscriptions handled by a separate billing
portal (TUI `$` opens it). The dead activation C# classes and the standalone `PlaceContext.Licensing`
project (plus its test project) have now been **deleted** from the tree and the solution.

**Portal** — reports render as HTML (Markdig) with a severity chart + action cards; quieter Host logs
(EF/framework → Warning); dependency/brain graph excludes MCP tool activity.

### Remaining / in progress
- **Portal job-creation UI + runtimes** (import-from-GitHub or in-editor; Python/.NET/Go/Ruby).
- **Redis** cache pod (shared cache; sticky still required for Blazor circuits).
- Open-source/setup **wiki** (`docs/SETUP.md` started).
- Optional: graduate the in-cluster job controller into a standalone **control-plane API** for multi-cluster.
- TUI: per-job **post-job action** toggles in the settings view (portal authoring exists; TUI shows outputs).

### Recently done
- ✓ **Run summary → search + dependency graph (local)**: every run's output is organized by Gemma and
  embedded by a local **Ollama `nomic-embed-text`** gateway (768-dim) into the pgvector
  `job_run_embeddings` store — so runs are semantically searchable and linked in the brain/dependency
  graph with no external key. Selected via `PlaceContext:Embeddings:Provider=ollama`.
- ✓ **Boot persistence**: `pctl ensure` idempotently starts/creates the k3d cluster, applies manifests,
  and waits for the Host rollout; `pctl autostart` installs a systemd **user** service running `ensure`
  on boot (prefers the k3d dev cluster even when k3s is also installed). Linger-enabled.
- ✓ **Post-job actions → MinIO** (per job: HTML report, inline-SVG chart, CSV, raw bundle). After a run,
  `PostJobActionService` builds outputs from its artifacts, stores them in the `placecontext-reports`
  bucket via the `IObjectStore`/MinIO adapter, and records `RunArtifactLink`s. Surfaced as openable links
  in the portal run-detail and the TUI run-detail; the Host streams them at `/runs/{id}/artifacts/{id}`
  (tenant-scoped). Configured on the portal job form. Best-effort — never fails the run. (MinIO uploads
  over plain HTTP: do **not** set `DisablePayloadSigning` — the SDK rejects it without HTTPS.)
- ✓ **Host Gemma in-cluster** (`deploy/k3s/ollama.yaml`): Ollama + `gemma3:4b` on a persistent model PVC,
  pulled once on boot, Ready-gated on model presence. Host wired via `PlaceContext:Llm:Provider=ollama`
  (env in `placecontext.yaml`) so `RunJobHandler` organizes each run's output through it (best-effort).
- ✓ **Vault portal page** (add/delete secrets UI) + the job form now lists the vault secret names that
  get injected as env vars at run time, with a link to manage them.
- ✓ TUI: **job→runs navigation** — runs list → per-run detail (per-shard console output, errors, artifacts).
  (✓ settings menu, ✓ run jobs from TUI, ✓ theme also changes font, ✓ per-job timeout.)
- Open-source/setup **wiki** (`docs/SETUP.md` started).
- Optional: graduate the in-cluster job controller into a standalone **control-plane API** for multi-cluster.
