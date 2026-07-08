# Jobs and artifacts

*Jobs run your code as sandboxed containers on the cluster — and they exist for exactly one reason: to generate artifacts.*

## The doctrine: the artifact IS the point

A job is **map** code (optionally with a **reduce** step) run as isolated containers. Whatever a
shard writes to STDOUT is captured as its **artifact** — that output is the job's reason to
exist. The TUI's job list leads with the ARTIFACTS column for the same reason; a job with no
artifacts is a job that hasn't done anything yet.

Emit **JSON**. When the artifact carries a numeric series, the portal and TUI chart it
automatically, and it feeds the global Reports view (see *Charts and reports*).

## The sandbox contract

Every shard runs as an isolated container with this exact contract:

| Aspect | Contract |
|---|---|
| **Input** | The shard's input payload arrives on **STDIN**. Read all of stdin and parse it (usually JSON). One run per payload in the job's input payloads; with no payloads you get one run with `{}` |
| **Files** | Your source files are mounted **read-only at `/work`**; the entrypoint is invoked as `/work/<entrypoint>` |
| **Output** | Write the result to **STDOUT** — it is captured as the shard's artifact. Keep logs on **STDERR** so they don't pollute the result |
| **Exit codes** | `0` = success by default (configurable). Extra codes can be mapped to *success* or *partial* per job |
| **Network** | **None by default** — a deny-all NetworkPolicy seals the run. Enable the job's network-egress toggle only when outbound access is genuinely required |
| **Timeout** | Default **300 s** per job, maximum **3600 s** (adjustable per job; the TUI steps it in 30 s increments) |
| **Dependencies** | None installed — no `pip install` / `npm install` pass. Vendor any libraries into your file set, or use a container image for dep-heavy jobs |

## Runtimes — python is the default

**Default to `python` unless you need another language** — it reads best for the data-shaping
work jobs do, and its stdlib covers json/csv/dates without dependencies.

| Runtime | Base image | Default entrypoint | Invoked as |
|---|---|---|---|
| `python` **(default)** | `python:3.12-slim` | `main.py` | `python /work/main.py` |
| `node` | `node:22-slim` | `index.js` | `node /work/index.js` |
| `go` | `golang:1.23-alpine` | `main.go` | `go run /work/main.go` |
| `ruby` | `ruby:3.3-slim` | `main.rb` | `ruby /work/main.rb` |
| `dotnet` | `mcr.microsoft.com/dotnet/sdk:10.0` | `main.cs` | `dotnet run /work/main.cs` (.NET 10 file-based app) |

`pctl doctor --go-live` smoke-tests runtimes by running a trivial sandboxed Job per runtime,
exactly how real jobs run — catching image-pull and sandbox regressions before they bite.

## A full python example

A job that summarises orders per day and emits chartable JSON:

```python
import sys, json, os

# 1. Input arrives on stdin — the shard's payload.
data = json.loads(sys.stdin.read() or "{}")

# 2. Secrets come from the vault as environment variables — never hard-code them.
api_key = os.environ.get("API_KEY")

# 3. Do the work.
totals = {}
for order in data.get("orders", []):
    day = order.get("day", "unknown")
    totals[day] = totals.get(day, 0) + order.get("amount", 0)

# 4. The artifact: JSON on stdout. A numeric map like this charts automatically.
print(json.dumps({"totals": totals}))

# Logs belong on stderr:
print(f"processed {len(data.get('orders', []))} orders", file=sys.stderr)
```

With an input payload of
`{"orders":[{"day":"mon","amount":12},{"day":"tue","amount":31},{"day":"mon","amount":5}]}`
the artifact is `{"totals": {"mon": 17, "tue": 31}}` — which renders as a bar chart in the run
detail, in the run history, and on the Reports page.

## Shards, concurrency, and reduce

- **Input payloads** — one JSON document per line in the job editor; **each line is one shard**.
  A run executes every shard (up to the concurrency limit in parallel, 1–32).
- **Reduce step** — an optional second stage that receives the shard results and produces one
  combined artifact. Its result appears separately in the run detail.
- **Input parameters** — a job may instead declare named parameters (`name` or `name|Label` per
  line). Running it then prompts for values (portal modal, or the `inputPayload` argument of the
  `run_job` MCP tool) and passes them as a single shard's payload.

## Environment variables and secrets

Plain configuration goes in the job's **env** (KEY=VALUE lines). **Credentials come from the
project Vault**: encrypted at rest, write-only in the UI, and injected into every run's
environment at execution time. A plaintext env var with the same name overrides the vault value.
The job editor lists which vault names will be injected, with a link to manage them.

## Post-job actions

After each run, the configured actions generate outputs **from the run's artifacts**, store them
in the object store (MinIO), and link them on the run:

| Action | Output |
|---|---|
| **HtmlReport** | The run's data rendered as a styled, self-contained HTML page — written by the local LLM, with a deterministic fallback |
| **Chart** | ONE chart (inline SVG) that best visualises the data — **drawn by the local LLM** from the actual values, deterministic fallback when the LLM is unavailable |
| **Csv** | The run's artifacts flattened into a downloadable CSV |
| **RawBundle** | Every produced output file stored as-is |

**Chart is enabled by default for new jobs created via MCP `upload_job_code`** — jobs exist to
generate artifacts, so new jobs chart their output out of the box. In the portal editor, tick the
actions you want; in the TUI, press `[s]` on a job to toggle each action (and network egress, and
the timeout) with checkboxes. Actions are best-effort: a failing action never fails the run.

## Creating a job

**Portal editor** — Jobs tab → **+ New job**. Choose the workload source:

- *Container image* — bring your own image (`myorg/worker:latest`) for dep-heavy work.
- *Inline code* — pick a runtime, paste source, set the entrypoint (or accept the default).

Then set input payloads, env, concurrency, exit-code policy, egress, parameters, and post-job
actions. Code jobs get a **⌁ Editor** button for a full editor page.

**MCP `upload_job_code`** — the agent path. Call `job_authoring_guide` first (it returns this
contract), then:

```json
{
  "projectId": "…",
  "jobName": "orders-per-day",
  "runtimeId": "python",
  "filesJson": "[{\"path\":\"main.py\",\"content\":\"…\"}]"
}
```

Targeting an existing job by `jobId` replaces its source and preserves everything else (payloads,
env, concurrency, reduce, exit-code policy). Targeting `projectId` + `jobName` creates the job if
absent, with sensible defaults: one `{}` shard, concurrency 1, success exit code 0, and the
**Chart** post-job action on. Multi-file uploads (paths may include subdirectories, e.g.
`lib/report.py`) require an explicit `entrypoint`.

## Running a job

| From | How |
|---|---|
| **Portal** | The **Run** button on the job card (prompts for declared parameters first). The run appears in the run history below |
| **TUI** | Select the job on the dashboard and press **`[R]`** — the run is queued and drained by the in-cluster scheduler. `⏎` drills into run history, then into a run's per-shard detail |
| **MCP** | `run_job` (waits for completion and returns the full run detail); `list_job_runs` / `get_job_run` fetch results later |
| **Schedule trigger** | `kind=Schedule` with a cron expression (5-field, or 6-field with seconds; evaluated in the workspace timezone), e.g. `0 0 * * *` for daily midnight |
| **Event trigger** | `kind=Event` fires whenever a named event is emitted — built-ins (`job.completed`, `activity.recorded`, `risk.recomputed`) or your own types via `define_event_type` + `emit_event`. The event payload is passed through as the run's parameters |

Triggers are managed on the Jobs tab (add / pause / delete) or via the MCP trigger tools.
Firing enqueues an independent run; concurrent runs are allowed.

## Reading results

A run records, per shard: exit code, outcome (Succeeded / Partial / Failed), the artifact, and the
log — plus the reduce result and a snapshot of the exact workload spec that ran. The portal's run
detail pretty-prints JSON artifacts, charts numeric series inline, and lists the post-job outputs
(report/chart/CSV/bundle) as links into the object store. In the TUI, a run detail renders the
same data with ASCII charts, and `[o]` / `[1–9]` open any links found in the output.

If you need to find old results by meaning rather than by date, `search_run_outputs` does
semantic (vector) search over a project's run outputs — when embeddings are configured.
