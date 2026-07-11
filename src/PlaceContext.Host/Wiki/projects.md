# Projects

*Create a project, run jobs against it, and let it accumulate the context, history, and data everything else builds on.*

## What a project is

A **project** is the top-level thing everything hangs off — usually one code repository. Each
project keeps its own context, history, jobs, database, and secrets, all private
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
| **Jobs** | Code you run to produce results (see *Jobs and artifacts*) |
| **Data** | The project's own private database with a SQL editor (see *Project data*) |
| **Brain** | The context and knowledge about the project — what's known, what was decided |
| **Activity Log** | The running history of every change: who did it, why, and whether it was verified |
| **Vault** | Encrypted secrets (API keys, passwords) that jobs can use without you exposing them |

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
2. Work gets done and recorded — so the history fills in with what happened and why.
3. Jobs run on schedules or triggers and produce results; charts and reports build up over time.

The result is a project that documents itself: the history says what happened and the reports
narrate it.

## See everything at a glance

The **Overview** page lists all your projects together, each with a risk band derived from its
history — how much churn it's seen, what shipped without being verified, and where the gaps are.
It's the fastest way to spot which project needs attention before you dive in.
