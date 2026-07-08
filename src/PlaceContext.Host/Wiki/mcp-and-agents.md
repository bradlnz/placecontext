# MCP and agents

*PlaceContext is an MCP server over Streamable HTTP — connect Claude (or any MCP client) and give agents real tools: the activity ledger, the knowledge graph, jobs, triggers, and reports.*

## Connecting a client

The MCP endpoint is **`/mcp`** on the portal origin — `http://localhost:7700/mcp` on a dev
cluster (`pctl url` prints it). Transport is **Streamable HTTP**; authentication is OAuth
(access tokens are RSA-signed with the cluster's `placecontext-oauth` key, so they validate
across replicas and restarts).

For Claude Code:

```bash
claude mcp add --transport http placecontext http://localhost:7700/mcp
```

then complete the authorization flow in the browser when prompted. Every tool call an agent makes
is timed and traced — watch it live in the portal's **MCP Inspector**, or in the TUI with `[m]`
(navigate with `↑↓`, `⏎` opens a call's full request/response).

If writes are rejected, call **`whoami`** first: it reports the token's embedded user id, tenant,
and role, and cross-checks them against the database — the standard diagnosis for a stale token
(role changed, or a user from a previous seed). Sign out/in of the portal and re-authorize the
MCP client to mint a fresh token.

## The tool surface

| Area | Tools |
|---|---|
| Diagnosis | `whoami` |
| Projects | `create_project`, `onboard`, `list_projects`, `register_project`, `get_project_overview` |
| Work items | `add_work_item`, `next_work_item`, `complete_work_item`, `list_work_items` |
| **The ledger** | `record_activity` — the write path for every change (see below), `get_timeline`, `recompute_risk` |
| Knowledge | `add_context`, `set_context`, `get_context`, `add_decision`, `query_graph`, `rebuild_graph`, `synthesize_context`, `suggest_improvements`, `scaffold_skill` |
| **Jobs** | `job_authoring_guide`, `upload_job_code`, `list_jobs`, `get_job`, `run_job`, `list_job_runs`, `get_job_run` |
| Triggers & events | `create_trigger`, `list_triggers`, `set_trigger_enabled`, `delete_trigger`, `define_event_type`, `emit_event`, `list_event_types`, `list_event_occurrences` |
| Reports | `generate_report`, `list_report_templates`, `define_report_template` |
| Search | `search_run_outputs` — semantic (vector) search over a project's job-run outputs; requires embeddings to be configured, returns empty otherwise |
| Usage | `record_usage` — LLM token counts and model names only (never code or prompts); powers the cost dashboards |

### Recording work through the ledger

`record_activity` is how a change becomes part of the project's history: it appends a ledger
entry (author, rationale, touched files/nodes, tests added, whether the architecture reviewer ran,
whether the change was live-verified, commit message) and makes a scoped git commit when the
project is a repo. The knowledge graph, risk scores, timeline, and reports are all built from
this stream — an agent that skips it leaves the project blind. The typical loop:

```text
get_context → next_work_item → (do the work) → record_activity → complete_work_item
```

### Prompts

Alongside tools, the server exposes MCP **prompts** — read-only guidance an agent can pull in so
it works against your standards:

| Prompt | Purpose |
|---|---|
| `onboard` | Load the project's context and requirements to start a session well-grounded |
| `review_work` | Review current work against the project's (and global) requirements and context |
| `record_activity_guidance` | Walk through recording a change so it passes the process-trust gates |
| `create_skill` | Guide creating a reusable skill/command for the project, in the target agent's format |

## The per-project agent (server-side)

Independent of any connected MCP client, the Host runs a **resident agent per project** on a
schedule. Each pass reviews the project's recent activity, open work queue, and recent job runs,
then uses the **local Ollama LLM** (Gemma, in-cluster) to assess where the project is and queue
up to three next-step work items — deduplicated against what's already open. Without an LLM, a
deterministic pass still queues the obvious: failed runs to investigate, an empty ledger to fill,
an overlong queue to triage.

Configuration:

```jsonc
"PlaceContext": {
  "Agent": {
    "Enabled": true,        // default true — set false to disable
    "IntervalMinutes": 60   // default 60, clamped to [5, 1440]
  }
}
```

One replica runs the pass per tick (Postgres advisory-lock leader election), across every
tenant's projects. The queued items appear on each project's board with a
"Proposed by the project agent" detail — connected agents can then claim them with
`next_work_item`, closing the loop: the platform proposes, the agent executes.

## Authoring a job over MCP, end to end

This is the canonical agent workflow — from nothing to a running, charting job in four calls.

**1. `job_authoring_guide`** — always call this first. It returns the sandbox contract (stdin
input, files read-only at `/work`, artifact on stdout, exit codes, no network by default), the
runtime table (**python is the default**; also node, go, ruby, dotnet), how vault secrets arrive
as env vars, and worked examples.

**2. Write code that honours the contract** — read stdin, print JSON:

```python
import sys, json
data = json.loads(sys.stdin.read() or "{}")
counts = {}
for item in data.get("items", []):
    counts[item.get("kind", "other")] = counts.get(item.get("kind", "other"), 0) + 1
print(json.dumps(counts))          # the artifact — a numeric map charts automatically
```

**3. `upload_job_code`** — create (or replace the source of) the job:

```json
{
  "projectId": "<project guid>",
  "jobName": "items-by-kind",
  "runtimeId": "python",
  "filesJson": "[{\"path\": \"main.py\", \"content\": \"import sys, json\\n...\"}]"
}
```

- Target an existing job with `jobId` instead — its payloads, env, concurrency, reduce step, and
  exit-code policy are preserved; only the source is replaced.
- Multi-file uploads may use subdirectories (`lib/report.py`) and must name an `entrypoint`.
- A **new** job is created with sensible defaults: one `{}` shard, concurrency 1, success exit
  code 0 — and the **Chart post-job action enabled**, so its output charts from run one.

**4. `run_job`** — execute and get the full result back in one call:

```json
{ "jobId": "<job guid>", "inputPayload": "{\"items\":[{\"kind\":\"a\"},{\"kind\":\"b\"},{\"kind\":\"a\"}]}" }
```

The response carries the overall status plus each shard's exit code, outcome, artifact, and log.
Because the artifact is a numeric series, it charts automatically: inline in the portal's run
detail, as the LLM-drawn Chart artifact in the run history, in the global Reports "Job data"
section, and as ASCII in the TUI. Use `list_job_runs` / `get_job_run` to fetch results later, and
`create_trigger` to put the job on a cron schedule or subscribe it to an event.

### Wiring jobs to events

Jobs become reactive by subscribing them to events:

```text
define_event_type  name="deploy.finished"  description="a deploy completed"   # once per workspace
create_trigger     jobId=…  name="post-deploy check"  kind="Event"  eventName="deploy.finished"
emit_event         name="deploy.finished"  payload="{\"env\":\"prod\"}"       # fires every subscribed trigger
```

The payload is passed through as the fired runs' parameters. Built-in events
(`job.completed`, `activity.recorded`, `risk.recomputed`) can be subscribed to the same way —
e.g. a summarising job that runs whenever any other job completes. `list_event_occurrences`
shows the recent event log.

## Good agent citizenship

- **Start with `get_context`** (or the `onboard` prompt) so the session is grounded in what's
  already known.
- **Record everything** through `record_activity` — with honest `testsAdded`,
  `architectureReviewerRun`, and `liveVerified` flags; process risk is scored from them.
- **Never put credentials in job code or env** — reference vault secret names; values are
  injected at run time.
- **Log usage** with `record_usage` (model + token counts only) so cost dashboards stay true.
- Remember every call is visible in the MCP Inspector — the trace is the audit trail.
