# PlaceContext — Jobs Automation Progress

Branch: `main` · all jobs-automation work merged
Last updated: 2026-07-20

Status: **222 tests passing** (4 Docker-gated skipped), all layers build clean.

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
| 16 | **Automatic value linking + duplicate warnings** | `RecordLink` index over identity-ish columns (address/email/phone/name/url) across all project tables; normalized exact matching; `record_links` table + `AddRecordLinks` migration; on-write refresh hooks for CRM records, CSV import, and data-map ingest; duplicate warnings on create/import (warn-only, rows kept); rescan button + linked-values list on Data Entities page; auto-linked records shown in EntityBrowse record detail. 70 new Application tests, full suite green. Also fixes the file-switch content-loss bug in `JobEditor.razor` (`DeleteFileAsync`). |
| 17 | **Agent Runner Phase 1 — Chat interface + persistence scaffold** | `IChatGateway` port + `OllamaChatGateway` / `NullChatGateway` adapters (config: `PlaceContext:Chat:Endpoint`/`Model`, default `qwen3.5:0.8b`). Domain entities: `AgentConfig` (project-scoped singleton), `AgentChatSession`, `AgentMessage`; repos `IAgentConfigRepository` / `IAgentChatSessionRepository`. EF rows + `AppDbContext` config (auto-migrated). CQRS: `GetAgentConfig` / `UpdateAgentConfig` / `SendAgentMessage` / `ListAgentChatSessions` / `GetAgentChatSession` handlers + views. `AgentContextBuilder` service does RAG: semantic search over run outputs + dependency graph summary injected into the system prompt. Facade methods on `IPlaceContextService`. MCP tools: `chat_with_agent`, `list_agent_sessions`, `get_agent_config`, `update_agent_config`. Permissions: `AgentsChat`, `AgentsManage`. Portal **Agents** page: config panel + chat UI + session sidebar. Menu item `project.agents`. 10 new handler tests + `FakeChatGateway` + in-memory repos. Full suite green. |

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

---

## 🤖 Agent Runner: Chat Interface + Local SLM Fine-Tuning (MLX)

**Goal:** Add an agent runner that acts as a chat interface connected to a local Small Language Model (SLM). The model will be fine-tuned on PlaceContext project data using Apple's MLX framework, configured from the dashboard. Once trained, the fine-tuned model drives business outcomes by interacting with project context, job outputs, and the dependency graph.

**Status:** Phase 1 shipped (chat interface + persistence scaffold); Phase 2 (MLX fine-tuning) planned  
**Branch:** `main` (merged)

### Motivation

Today, run outputs are organized by an external or local LLM and stored in the dependency graph as searchable memory. A dedicated agent runner will expose this memory through a conversational UI and let customers specialize the model on their own PlaceContext data so answers and actions are tuned to their domain.

### Architecture Decisions

1. **Local-first inference**
   - The agent connects to a local SLM served by **Ollama** or **llama.cpp** (reusing the existing `OllamaLlmGateway` infrastructure).
   - No external API key is required after the model is downloaded.

2. **Fine-tuning with MLX**
   - On Apple Silicon, use **MLX** (`mlx`, `mlx-lm`) for efficient LoRA/QLoRA fine-tuning of a small base model (e.g. `Qwen2.5-3B-Instruct`, `Llama-3.2-3B`, or `Gemma-3-4B-it`).
   - On non-Apple hardware, fall back to **Unsloth** / **PEFT** + **bitsandbytes** with the same training recipe so the feature is not Mac-only.
   - Fine-tuning is triggered from the dashboard as an async **job** (reusing the Jobs/Run infrastructure) so it runs in-cluster or locally with the same artifact/secret/queue semantics.

3. **Training data from PlaceContext**
   - Generate a supervised fine-tuning (SFT) dataset from:
     - organized run outputs (`job_run_embeddings` + raw artifacts),
     - project activity/decisions,
     - entity records and record links,
     - existing brain/dependency graph Q&A pairs.
   - Dataset format: ShareGPT / OpenAI chat-completion JSONL.
   - Dataset is versioned and stored as a run artifact / MinIO object.

4. **Dashboard configuration**
   - New **Agents** page in the portal:
     - select base model,
     - tune hyperparameters (epochs, learning rate, LoRA rank, max seq length),
     - pick data sources (projects, run tags, entity types),
     - start fine-tuning run,
     - view progress, loss curves, eval metrics,
     - activate a fine-tuned checkpoint for chat.
   - Configuration persisted as `AgentConfig` / `AgentModel` domain entities.

5. **Chat interface**
   - New MCP tool(s) and portal page for chat:
     - `chat_with_agent` / `send_agent_message`,
     - streaming responses,
     - tool-calling support so the agent can invoke existing PlaceContext tools (search run outputs, list jobs, emit events, enqueue runs).
   - The agent's system prompt injects relevant context retrieved via the existing embedding/search layer (RAG over run outputs + dependency graph).

6. **Driving business outcomes**
   - Agent can be given goals/outcomes via the dashboard (e.g. "watch for duplicate leads", "summarize weekly activity", "suggest next action").
   - Outcomes are implemented as event-triggers or scheduled jobs that call the active agent model.
   - Fine-tuned weights + adapter config are exposed as a job secret/env so other jobs can load the same model.

### Domain / Data Model (planned)

| Entity | Purpose |
|---|---|
| `AgentConfig` | Dashboard config: base model, hyperparameters, data-source filters, active checkpoint id. |
| `AgentTrainingRun` | One fine-tuning run: dataset artifact, checkpoint artifact, status, metrics. |
| `AgentCheckpoint` | A produced adapter + merged weights (or GGUF) stored as artifacts. |
| `AgentChatSession` | Per-user chat thread + message history. |
| `AgentOutcome` | Business outcome definition: trigger, prompt template, enabled flag. |

### Phases / Remaining Work

| # | Phase | Deliverables |
|---|---|---|
| 1 | **Spike** | Validate MLX LoRA fine-tuning on sample PlaceContext data; pick base model; confirm Ollama can serve merged/adapter weights. |
| 2 | **Dataset pipeline** | `BuildAgentDatasetJob` (or handler) that exports project data to JSONL; unit tests; artifact storage. |
| 3 | **Fine-tune job** | `FineTuneAgentJob` using MLX (Apple) and Unsloth/PEFT fallback; hyperparameters from `AgentConfig`; metrics logging. |
| 4 | **Domain + persistence** | ✅ `AgentConfig`, `AgentChatSession`, `AgentMessage` entities + repos + EF rows + AppDbContext config; CQRS commands/queries/handlers; `IChatGateway` port + Ollama/Null adapters. *(AgentTrainingRun, AgentCheckpoint, AgentOutcome deferred to Phase 2.)* |
| 5 | **Dashboard UI** | ✅ **Agents** portal page: config panel (model, prompt, context chunks, enabled toggle) + chat UI + session sidebar. |
| 6 | **Chat interface** | ✅ Portal chat page + `chat_with_agent` MCP tool; RAG context injection via `AgentContextBuilder` (embedding search + graph summary); `list_agent_sessions`, `get_agent_config`, `update_agent_config` MCP tools. Permissions: `AgentsChat`, `AgentsManage`. |
| 7 | **Outcomes / automation** | `AgentOutcome` entity; schedule/event triggers that invoke the active agent; action handlers to enqueue jobs/emit events. |
| 8 | **Self-host packaging** | k3s manifest for MLX-capable node selector / GPU scheduling; docs update; TUI integration. |

### Open Questions / Notes

- **MLX availability:** MLX is Apple-only; the Linux fallback must be solid for self-hosting on k3s/Linux.
- **Model licensing:** Fine-tuned derivatives of permissive base models only; track base-model license in `AgentConfig`.
- **Data privacy:** Training data stays in the tenant's Postgres/MinIO; no phone-home for fine-tuning.
- **Compute:** Fine-tuning is CPU/GPU heavy; runs should respect job timeout, resource requests/limits, and node selectors.
- **Reusability:** The existing `IEmbeddingGateway`, `ILlmGateway`, `IWorkloadRunner`, run artifacts, and event/trigger plumbing should be reused as much as possible.
- **Evaluation:** Add a held-out eval split and perplexity/BERTScore metric; surfaced per checkpoint.
