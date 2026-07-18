# MCP and agents

*Connect Claude (or another AI agent) to PlaceContext and let it do real work on your projects — record changes, author and run jobs, generate reports, and more.*

## Connect your agent

PlaceContext exposes a single endpoint your AI agent connects to. On a dev machine it's
`http://localhost:7700/mcp` — run `pctl url` to print it.

For **Claude Code**:

```bash
claude mcp add --transport http placecontext http://localhost:7700/mcp
```

Then finish signing in when the browser prompts you. That's it — the agent now has real tools it
can use against your projects.

Everything the agent does is visible to you, live: watch it in the portal's **MCP Server** page,
or in the TUI with **`[m]`** (`↑↓` to move, `⏎` to open a call's full request and response). That
trace is your audit trail.

If the agent's changes are unexpectedly rejected, it's almost always a stale sign-in from an old
session. Sign out and back in of the portal, reconnect the agent to get a fresh one, and try
again.

## What an agent can do for you

Once connected, an agent can work a project the way you would:

- **Set up a project** — create it by name and path; creating the same one twice just returns it.
- **Build and run jobs** — write a job, upload it, run it, and put it on a schedule, an event
  trigger, or a chain.
- **Query the project's data** — run safe, **SELECT-only** queries against the project's own
  database (it can never write, or reach another project's tables).
- **Build charts** — save SQL-backed charts over any table, which then appear on the Analytics
  tab and the Dashboard.
- **Tag and organise data** — define entities so tagged records, artifacts, and runs link
  together into the business views (see *Entities and insights*).

You don't need to memorize tool names — the agent discovers them. Everything runs locally; there
is no cloud model in the loop, and jobs never call out to an LLM.

## Walkthrough: an agent builds and runs a job

Here's the whole arc, from nothing to a running, self-charting job.

**1. The agent reads the job guide first.** It returns the rules — read the input from standard
input, print your result, no network by default, and Python is the default language — plus worked
examples.

**2. It writes code that follows the contract** — read the input, print JSON:

```python
import sys, json
data = json.loads(sys.stdin.read() or "{}")
counts = {}
for item in data.get("items", []):
    counts[item.get("kind", "other")] = counts.get(item.get("kind", "other"), 0) + 1
print(json.dumps(counts))          # a map of numbers — charts automatically
```

**3. It uploads the job.** A brand-new job comes with sensible defaults and a **Chart** return
type, so it produces a chart from its very first run. Uploading again to an existing job just
replaces the code and keeps all its settings.

**4. It runs the job** with some input and gets the full result back — each run's outcome, its
result, and its log. Because the result is a series of numbers, it charts everywhere: in the run
detail, in the Artifacts viewer, and as ASCII in the TUI.

From there the agent can put the job on a schedule, or wire it to fire on an event.

### Wiring a job to events

Jobs can react to things happening. An agent defines an event once, subscribes a job to it, and
then anything that emits that event fires the job — passing the event's details through as the
run's input:

```text
define a "deploy.finished" event  (once)
subscribe the "post-deploy check" job to it
emit "deploy.finished"  →  fires every subscribed job
```

Built-in events work the same way — a job can run whenever another job finishes or whenever a
change is recorded.

## Good habits for agents

- **Return a typed artifact** — declare the job's return type so every run stores an openable
  result; print pure JSON (or write the file to `/out`) and keep diagnostics on standard error.
- **Never put secrets in job code** — reference the project Vault's secret names; the real values
  are supplied at run time.
- **Keep queries SELECT-only** — the data and chart tools refuse anything that writes; aggregate
  rather than dumping raw rows.
- **Remember it's all visible** — every action shows up on the MCP Server page.

## Any MCP client works

While the examples above use Claude Code, the endpoint is standard, so any MCP-capable agent can
connect the same way and use the same tools. Point it at the address `pctl url` prints, complete
the sign-in, and it's ready to work on your projects.

Whichever agent you use, the picture stays the same: you connect it once, it works your projects
with real tools, and you keep full visibility over everything it does.
