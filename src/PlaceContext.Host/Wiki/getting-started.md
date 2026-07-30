# Getting started

*A quick guide to the PlaceContext portal.*

## What PlaceContext does

PlaceContext keeps project context, jobs, data, artifacts, and agent activity in one workspace.
Jobs run in isolated containers on your own cluster, and AI agents connect through MCP.

## First steps

1. Open **Onboarding** and copy the MCP endpoint.
2. Connect an MCP client and ask it to onboard a project.
3. Select the project from the project switcher.
4. Open **Jobs**, create a job, and run it.
5. Watch the run in **Jobs** or **Observability**, then open its files in **Artifacts**.

## Main areas

| Area | Purpose |
|---|---|
| **Dashboard** | Workspace totals, recent runs, charts, and pinned entities |
| **Project overview** | Project activity, decisions, and dependency graph |
| **Jobs** | Create, edit, run, and monitor jobs |
| **Chains** | Build multi-stage job pipelines |
| **Schedules** | Manage scheduled, event, and launchpad triggers |
| **Data** | Tables, SQL analytics, data mappings, and entities |
| **Vault** | Store encrypted project secrets |
| **Events** | View event types and recent occurrences |
| **Chat** | Ask the selected project's agent questions and run tools |
| **Artifacts** | Browse files produced by job runs |
| **Observability** | Review job and chain runs across projects |
| **Cluster** | Check nodes and add workers |

The menu is permission-aware and can be renamed or reordered under **Settings → Menu**.

## A useful working loop

Create or update code, record the change through the agent, run a job, inspect its output, and
map useful results into project tables. Use chains, schedules, and events when the workflow
should run without manual steps.
