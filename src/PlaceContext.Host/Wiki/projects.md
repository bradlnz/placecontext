# Projects

*Create a project, run jobs against it, and let it accumulate the data and results everything else builds on.*

## What a project is

A **project** is the top-level thing everything hangs off — usually one code repository. Each
project keeps its own jobs, database, tagged entities, artifacts, and secrets, all private
to it.

Create one from the portal's **Projects overview** page (**+ New project**), or have an AI agent
create it for you (see *MCP and agents*). Creating the same project twice is safe — you just get
the existing one back.

## What's inside a project

Select a project and its pages appear in the nav:

| Area | What it's for |
|---|---|
| **Jobs** | Code you run to produce results (see *Jobs and artifacts*) |
| **Chains** | Multi-step pipelines that run several jobs in sequence, passing results along |
| **Schedules** | Cron and event triggers that fire jobs automatically |
| **Data** | The project's own private database, plus Analytics, Data map, and Entities (see *Project data* and *Entities and insights*) |
| **Vault** | Encrypted secrets (API keys, passwords) that jobs can use without you exposing them |

Every result those jobs produce is collected in the workspace-wide **Artifacts** viewer, and
each run's progress is visible under **Observability**.

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

## Turn data into business views

As jobs load data and produce results, you tag it into **entities** — *Sites*, *Feasibility*,
whatever your domain calls for. Each tagged entity becomes its own business view in the nav,
with records, a relationship graph, and SQL-backed analytics. This is how a project turns raw
job output into something the business can read at a glance — see *Entities and insights*.

## A typical loop

1. Create the project (or have an agent create it for you).
2. Jobs run on schedules or triggers, load data into the project's tables, and produce artifacts.
3. Tag that data into entities; the business views, graphs, and charts build themselves.

The result is a project that organises itself: the jobs supply the data, and the entity layer
turns it into insight.

## See everything at a glance

The **Projects overview** page lists all your projects together and their recent activity —
the fastest way to spot which project needs attention before you dive in. The **Dashboard**
shows the current project's live run stats and pinned entity views.
