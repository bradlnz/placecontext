# Projects

*Create a project, fill its board with work, run jobs against it, and let a built-in agent keep suggesting what to do next.*

## What a project is

A **project** is the top-level thing everything hangs off — usually one code repository. Each
project keeps its own context, history, work board, jobs, database, and secrets, all private
to it.

Create one from the portal's **Overview** page (**+ New project**), or have an AI agent create
it for you (see *MCP and agents*). Creating the same project twice is safe — you just get the
existing one back. If you point PlaceContext at a git repo, an agent can also **onboard** it in
one step: it reads your recent commits and your README/AGENTS/CLAUDE docs to seed the project's
context and history, so it starts out already knowing something about your code.

## What's inside a project

Open a project and you get:

| Area | What it's for |
|---|---|
| **Board** | Your queue of work items — Low, Normal, or High priority |
| **Jobs** | Code you run to produce results (see *Jobs and artifacts*) |
| **Data** | The project's own private database with a SQL editor (see *Project data*) |
| **Brain** | The context and knowledge about the project — what's known, what was decided |
| **Activity Log** | The running history of every change: who did it, why, and whether it was verified |
| **Vault** | Encrypted secrets (API keys, passwords) that jobs can use without you exposing them |

## The work board

The board is a simple, prioritised queue. Every item moves through three states:

| State | Meaning |
|---|---|
| **Queued** | Waiting. Ordered by priority, then by age |
| **In progress** | Someone (or an agent) has claimed it and is working on it |
| **Done** | Finished, and the change was recorded in the activity log |

Add items yourself, or let the built-in agent and your reports propose them. Closing items
matters: the agent won't suggest something that's already on the board, so completing work is
what lets fresh suggestions come through.

## Store secrets in the Vault

Every project has a **Vault** for its sensitive values — API keys, passwords, tokens. Add them
by name, and they're encrypted and can't be read back in the UI. When a job runs, the vault's
secrets are handed to it as environment variables, so your job code references them **by name**
and never contains the actual value. This is the right home for anything you wouldn't want
sitting in plain text — see *Jobs and artifacts* for using them from a job.

## Run jobs automatically

A project's jobs don't have to be run by hand. On the **Jobs** tab you can put a job on a
schedule (say, nightly), or have it fire whenever something happens — another job finishing, a
change being recorded, or an event you define yourself. That's how a project keeps its data and
charts fresh without anyone lifting a finger. See *Jobs and artifacts* for the details.

## Let the project suggest its own next steps

Every project has a **built-in agent** that quietly reviews where things stand and adds
suggested next steps to the board for you. On each pass it looks at:

- the most recent changes recorded in the activity log,
- what's already open on the board, and
- the project's most recent job runs.

It then proposes at most **three** next steps, each with a title, some detail, and a priority.
It never repeats a suggestion that's already open, so the board stays useful rather than noisy.
Suggested items are labelled *"Proposed by the project agent"* so you always know where they
came from.

If the local AI model is turned off, the agent still surfaces the obvious, concrete signals:

| It notices | It queues | Priority |
|---|---|---|
| Recent job runs failed | "Investigate N failed job run(s)" | High |
| Nothing recorded yet | "Record the project's recent work in the activity log" | Normal |
| More than 5 items waiting | "Triage the work queue" | Low |

### Change how often it runs

By default the agent reviews each project once an hour. To change the interval or turn it off,
set these on the deployment:

```bash
kubectl -n placecontext set env deploy/placecontext \
  PlaceContext__Agent__Enabled=true \
  PlaceContext__Agent__IntervalMinutes=30
```

`Enabled` defaults to on; `IntervalMinutes` defaults to 60 and can be anywhere from 5 minutes
to a full day.

## Your projects stay private from each other

Each project is completely walled off. Its data, jobs, secrets, and history belong only to it —
**one project can never see another's tables or work**. The same holds at the team level: each
workspace is isolated, so different teams sharing the same PlaceContext install never see each
other's projects.

If an agent's changes are unexpectedly rejected, it's almost always a stale sign-in from an old
session. Signing out and back in, then reconnecting the agent, mints a fresh one.

## Explore what's known: the Brain

The **Brain** page is where a project's durable knowledge lives:

- **Context** — a living document describing the project. It's the first thing an agent reads
  before touching anything, and you can edit it directly.
- **Decisions** — a lightweight record of important choices: the question, the choice made, and
  why.
- **The knowledge map** — built from your recorded activity, it answers questions like *where is
  the churn concentrated?*, *what did we decide?*, and *what shipped without being verified?*

The TUI's `/` search runs over this same knowledge, so you can find a decision or a note from
the dashboard without opening the portal.

## A typical loop

1. Create the project (or have an agent onboard your repo).
2. Work gets claimed off the board, done, and recorded — so the history fills in with what
   happened and why.
3. Jobs run on schedules or triggers and produce results; charts and reports build up over time.
4. The built-in agent reviews everything each interval and keeps the board topped up with the
   next most useful steps.

The result is a project that documents itself: the history says what happened, the reports
narrate it, and the board always shows what to do next.

## See everything at a glance

The **Overview** page lists all your projects together, each with a risk band derived from its
history — how much churn it's seen, what shipped without being verified, and where the gaps are.
It's the fastest way to spot which project needs attention before you dive in.
