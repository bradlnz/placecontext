# Projects

*Projects are the top-level unit: each carries its context, activity ledger, work board, jobs, its own database, a vault, and a resident agent that proposes next steps.*

## What a project is

Everything in PlaceContext hangs off a **project** — usually a code repository, registered by its
absolute path. Create one from the portal, or from an agent over MCP:

```text
create_project  path=/home/you/code/myapp             # idempotent — re-creating returns the existing project
onboard         path=/home/you/code/myapp             # create + backfill git history + seed context + scaffold a skill
```

`onboard` is the one-call bootstrap: it creates the project, backfills the activity log from
recent git commits (default 50), seeds context from README/AGENTS/CLAUDE docs, and scaffolds a
local skill for your AI agent (Claude Code or Codex).

## What every project carries

| Facet | Where | What it is |
|---|---|---|
| **Context / knowledge (Brain)** | Brain page, `get_context` / `add_context` / `set_context` | A durable Markdown context document plus a knowledge graph rebuilt from logged activity (`rebuild_graph`, `query_graph`) |
| **Activity log** | Activity Log page, `record_activity` | The ledger: every change with author, rationale, touched files/nodes, tests added, and verification flags. Risk scoring builds on it |
| **Work-item board** | Project page, `add_work_item` / `next_work_item` / `complete_work_item` | A prioritised queue (Low / Normal / High). Agents claim the next item, do the work, record it, and complete it |
| **Decisions** | Brain, `add_decision` | ADR-lite records: question, choice, rationale |
| **Jobs** | Jobs tab | Sandboxed code workloads that generate artifacts (see *Jobs and artifacts*) |
| **Data** | Data tab | The project's **own database** — a private Postgres schema with a SQL editor (see *Project data*) |
| **Vault** | Vault page | Encrypted secrets (API keys, passwords), injected into job runs as environment variables |
| **Triggers & events** | Jobs tab, `create_trigger` / `emit_event` | Cron schedules and event subscriptions that run jobs automatically |
| **Risk** | Overview, `recompute_risk` | Technical + process risk computed from the ledger |

## The per-project agent

Each project has a resident agent that periodically reviews where the project is and **queues
next-step work items onto the board**. One pass looks at:

- the 10 most recent activity records,
- the open work queue,
- the project's 10 most recent job runs.

It then asks the **local LLM** (Ollama + Gemma, in-cluster — nothing leaves your machines) for an
assessment and at most **3 next steps**, each with a title, detail, and priority. Suggestions are
**deduplicated against everything already open** — the agent never nags the same step twice.
Queued items are tagged in their detail with `Proposed by the project agent — <assessment>`.

When no LLM is configured (or the call fails), a deterministic pass still surfaces the
objectively actionable signals:

| Signal | Queued item | Priority |
|---|---|---|
| Recent runs ended in `Failed` | "Investigate N failed job run(s)" | High |
| No activity recorded at all | "Record the project's recent work in the activity log" | Normal |
| More than 5 open work items | "Triage the work queue" | Low |

### Scheduling and configuration

A background scheduler drives the agent across **every tenant's projects** on an interval. Only
one Host replica runs a pass at a time (leader election via a Postgres advisory lock), and each
project gets a fresh scope so one failure can't poison the rest.

```jsonc
// appsettings / environment
"PlaceContext": {
  "Agent": {
    "Enabled": true,          // default true; set false to disable the agent entirely
    "IntervalMinutes": 60     // default 60; clamped to [5, 1440]
  }
}
```

As environment variables on the deployment:

```bash
kubectl -n placecontext set env deploy/placecontext \
  PlaceContext__Agent__Enabled=true \
  PlaceContext__Agent__IntervalMinutes=30
```

## Getting oriented fast

Two MCP tools compress a whole project into something an agent (or you) can act on:

- **`synthesize_context`** — pulls *all* accumulated context (context doc, requirements,
  decisions, work items, activity, risk) into one structured brief ending in a prioritised action
  plan. Pass `createWorkItems=true` to queue that plan onto the board.
- **`suggest_improvements`** — prioritised improvements derived from logged activity: churn
  hotspots, unverified changes, missing context, risk signals.

From the portal, the **Reports** page generates defined reports from the same accumulated data
(see *Charts and reports*).

## Multi-tenancy

PlaceContext is multi-tenant: each **workspace (tenant) is isolated**. Projects, work items,
activity, jobs, secrets, report templates, and event types all belong to a tenant, and MCP access
tokens embed the tenant they were minted for. The `whoami` MCP tool reports the calling token's
user id, tenant, and role, and cross-checks them against the database — the first thing to reach
for when writes are unexpectedly rejected (a stale token from a previous session shows up here).

Isolation extends below the application layer too:

- Every project's Data tab is a **separate Postgres schema and role** — a project cannot read
  another project's tables, or the platform's (see *Project data*).
- On the fleet side, `pctl mesh tenant add <id>` gives each customer an ACL-isolated private
  network — one tenant's nodes never see another's.

## The Brain: context and the knowledge graph

Two layers of durable knowledge live behind the Brain page:

- **The context document** — one Markdown document per project, the thing an agent reads before
  touching anything. `add_context` appends a section; `set_context` rewrites the whole document
  (e.g. after consolidating notes); `get_context` fetches it at session start.
- **The knowledge graph** — rebuilt from logged activity (decisions, changes, tool calls) with
  `rebuild_graph` (incremental by default). Ask it structured questions with `query_graph`:

```text
query_graph  projectId=…  question="hotspots"     # churn concentrations
query_graph  projectId=…  question="decisions"    # the ADR-lite record
query_graph  projectId=…  question="unverified"   # changes shipped without verification
query_graph  projectId=…  question="activity"     # the recent change stream
```

The TUI's `/` search runs over the same contents — decisions, context, and activity.

## The work-item lifecycle

| State | Entered by | Meaning |
|---|---|---|
| **Queued** | `add_work_item` (a person, an agent, a report's action plan, or the project agent) | Waiting, ordered by priority then age |
| **In progress** | `next_work_item` | Claimed — the tool returns the highest-priority, oldest queued item and marks it |
| **Done** | `complete_work_item` | Finished — after the change was recorded via `record_activity` |

`list_work_items` shows all three states. The dedupe rule matters here: the project agent
compares proposed titles (case-insensitively) against everything not Done, so closing items is
what lets fresh, different proposals through.

## A typical loop

1. `onboard` the repo (or create the project in the portal).
2. Agents `get_context` at session start, claim work with `next_work_item`, do the change, then
   `record_activity` with rationale and verification flags, and `complete_work_item`.
3. Jobs run on schedules or events and generate artifacts; charts and reports accumulate.
4. The per-project agent reviews the state every interval and keeps the board topped up with the
   next most useful steps.

The result is a project that documents itself: the ledger records what happened and why, the
graph and risk scores are derived from it, the reports narrate it, and the board always shows
what should happen next.
