# PlaceContext — Jobs Automation Progress

Branch: `feat/job-triggers-and-events` · Base: `main`
Last updated: 2026-06-25

Status: **198 tests passing** (4 Docker-gated skipped), all layers build clean.

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
| 12b | **Self-host on k3s** | `Dockerfile` (Host image); `deploy/k3s/` (Postgres **pgvector** + Host Deployment/Service/Ingress, activation enforced via secret); `deploy/selfhost.sh --activation-key <KEY>` installs k3s, deploys, and prints the worker-node join command. |

Commits: `6411749` triggers/events layer → `aec4860` MCP → `1450683` UI → `1240f6a`
parameters/modal → `6c29f00` artifacts → `4ae1900` configurable LLM → `81a4fbb` setup.sh.

---

## ⏳ Remaining (tracked, with decisions)

### #9 — Vectorize run output → dependency graph  *(in progress)*
Pipeline: job completes → (configurable LLM) **organizes** output → **Voyage AI** embeds it →
stored in the **dependency graph** as queryable nodes.
- **Decision made:** use **pgvector** in the existing Postgres.
- Recon: the graph is assembled **on-read** from activity log + decisions + tool calls
  (`DecisionTreeAssembler`); only a `GraphSnapshotRef` is persisted (`projects.GraphJson`);
  **no pgvector / no embeddings exist yet**.
- **To build:** `IEmbeddingGateway` port + `VoyageEmbeddingGateway` + Null fallback (provider
  pattern, keyed by `PlaceContext:Voyage:ApiKey`); enable the `vector` extension (needs the DB
  image switched to `pgvector/pgvector:pg16` in `run.sh`/`setup.sh`); a tenant-owned
  `job_run_embeddings` table (text + `vector(N)` column); a repo with cosine nearest-neighbour
  search; embed organized run output in `RunJobHandler`; a `search_run_outputs` query + MCP tool;
  optionally a `JobRunOutput` node kind in the assembled graph.

### #10 — Deploy on k3s
A Kubernetes-Job-based `IWorkloadRunner` (shards run as k8s Jobs); the in-process trigger scheduler
needs **leader-election** (or run as a singleton Deployment) so schedules fire once across replicas.

### #11 — External event sources + parameter injection
Webhook ingress + an external queue listener (e.g. **Cloudflare Queue**) that push events in; event
types carry **field definitions**; a fired event injects its payload fields as run **parameters**.
Plumbing started: `QueuedJobRun.Payload` + `RunJobCommand.InputPayload` already flow a payload through.

### #12 — Self-host CLI (changed from Terraform), gated by an activation code
A CLI customers run to self-host: pulls the published image, stands up k3s, applies config/migrations,
and gates usage by an **activation code** (validate against a licensing service or signed offline token;
enforce at startup; surface activation state in the portal).

### #13 — Folder reorganization for human maintainability
**Decided:** by **feature/vertical-slice** grouping (`Application/Jobs/`, `/Triggers/`, `/Events/`,
`/Reports/`, `/Projects/`, `/Risk/` — command+handler+query+DTO+mapper per slice). Preserve
one-class-per-file + Onion boundaries; update namespaces. **Do AFTER #9–12** to avoid churn.

---

## Notes
- Multi-tenancy: every new row is `ITenantOwned` with an EF global query filter; the background
  scheduler sets the ambient `CurrentTenant` (AsyncLocal) per unit of work before dispatching.
- Schema is **EF migrations** (not `EnsureCreated`) — never drop the dev DB; add a migration.
- Convention: **one top-level type per file**.
