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

Everything the agent does is visible to you, live: watch it in the portal's **MCP Inspector**, or
in the TUI with **`[m]`** (`↑↓` to move, `⏎` to open a call's full request and response). That
trace is your audit trail.

If the agent's changes are unexpectedly rejected, it's almost always a stale sign-in from an old
session. Sign out and back in of the portal, reconnect the agent to get a fresh one, and try
again.

## What an agent can do for you

Once connected, an agent can work a project the way you would:

- **Set up a project** — create it, or onboard a git repo so it starts already knowing your
  recent history and docs.
- **Work the board** — claim the next item, do the work, and mark it done.
- **Record what it did** — every change goes into the project's history with the reasoning behind
  it, so the knowledge, risk scores, and reports all stay current. This is the one habit that
  matters most: a change that isn't recorded leaves the project blind.
- **Build and run jobs** — write a job, upload it, run it, and put it on a schedule or trigger.
- **Manage knowledge** — read and update the project's context, record decisions, and ask
  structured questions about where the risk and churn are.
- **Generate reports** — produce a written report from everything the project knows.
- **Get oriented fast** — pull all of a project's accumulated context into one brief with a
  prioritised action plan, or ask for a ranked list of suggested improvements.

You don't need to memorize tool names — the agent discovers them. The server also offers ready-made
guidance the agent can pull in so it works to your standards: how to start a session grounded in
your project, how to review its work against your requirements, and how to record a change
properly.

### A good session, start to finish

A well-behaved agent follows the same loop you would:

1. Read the project's context so it knows what's already going on.
2. Claim the next item off the board.
3. Do the work.
4. Record the change — with the reasoning, and honest notes on what was tested and verified.
5. Mark the item done.

Because every step is recorded, the project's history, knowledge, risk scores, and reports all
stay accurate on their own. You can watch the whole thing unfold in the MCP Inspector.

## Let the project work on itself

Separately from any agent you connect, each project has a **built-in agent** that reviews it on a
schedule and adds up to three suggested next steps to the board — never repeating what's already
there. So the loop closes on its own: the project proposes work, and your connected agent (or you)
picks it up. See *Projects* for how to tune how often it runs.

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

**3. It uploads the job.** A brand-new job comes with sensible defaults and charting turned on,
so it produces a chart from its very first run. Uploading again to an existing job just replaces
the code and keeps all its settings.

**4. It runs the job** with some input and gets the full result back — each run's outcome, its
result, and its log. Because the result is a series of numbers, it charts everywhere: in the run
detail, in the run history, on the Reports page, and as ASCII in the TUI.

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

Built-in events work the same way — a job can run whenever another job finishes, whenever a
change is recorded, or whenever risk is recalculated.

## Good habits for agents

- **Start grounded** — read the project's context before touching anything.
- **Record everything** — with honest notes on whether tests were added and whether the change
  was actually verified. Those flags feed the project's risk scores.
- **Never put secrets in job code** — reference the project Vault's secret names; the real values
  are supplied at run time.
- **Log usage** — model names and token counts only (never your code or prompts) so the cost
  dashboards stay accurate.
- **Remember it's all visible** — every action shows up in the MCP Inspector.

## Any MCP client works

While the examples above use Claude Code, the endpoint is standard, so any MCP-capable agent can
connect the same way and use the same tools. Point it at the address `pctl url` prints, complete
the sign-in, and it's ready to work on your projects.

Whichever agent you use, the picture stays the same: you connect it once, it works your projects
with real tools, and you keep full visibility over everything it does.
