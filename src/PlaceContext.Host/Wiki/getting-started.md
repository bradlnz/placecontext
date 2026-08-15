# Getting started

*A quick guide to the PlaceContext portal.*

## What PlaceContext does

PlaceContext keeps project work, customer information, automated tasks, saved files, and agent
activity in one workspace.

You do not need to understand the technical parts to use the portal. A **job** is a saved task, a
**chain** joins several tasks into a workflow, and an **artifact** is a file produced by a task.

## First steps

1. If the workspace has no projects, connect an approved MCP client and ask it to onboard a project.
2. Select the project from the switcher in the lower-left corner.
3. Open **Jobs** to run a saved task or **Data** to explore reusable outputs.
4. Follow progress in **Jobs** or **Observability**, then open **Artifacts** for files produced by completed tasks.

## Connect an MCP client

Your MCP endpoint is your PlaceContext workspace URL followed by `/mcp`:

```text
<workspace-url>/mcp
```

For Claude Code, add it with:

```bash
claude mcp add --transport http placecontext <workspace-url>/mcp
```

The first tool call opens PlaceContext in your browser so you can sign in and authorize access. The
client receives a token scoped to this workspace and renews it automatically while active. Other MCP
clients can use the same endpoint with Streamable HTTP; OAuth is discovered automatically.

## Add the first project

Ask the connected client to **“onboard this project into PlaceContext.”** The onboarding tools register
the repository, import its git history, seed context from project documentation, and prepare the client
to record activities and decisions. A project is the boundary for jobs, reusable data, entities,
analytics, permissions, and provenance.

Once the project exists, ask the client to load the project overview at the beginning of work and record
activities and decisions as work progresses. Use **Jobs** for repeatable computation and **Chains** for
multi-stage workflows; mapped outputs remain available through project tables, entities, analytics, and
search.

## Main areas

| Area | Purpose |
|---|---|
| **Dashboard** | Workspace totals, recent runs, charts, and pinned entities |
| **Project overview** | Project activity, decisions, and dependency graph |
| **Jobs** | Create, edit, run, and monitor jobs |
| **Tests** | Check a job with saved example inputs before using it in a workflow |
| **Chains** | Build multi-stage job pipelines |
| **Schedules** | Manage scheduled, event, and launchpad triggers |
| **Data** | Tables, SQL analytics, data mappings, entities, and searchable project data |
| **Vault** | Store encrypted project secrets |
| **Events** | View event types and recent occurrences |
| **Agents** | Configure the Command Agent, collaborating worker agents, capabilities, and Job access |
| **Chat** | Ask the selected project's agent questions and run tools |
| **Artifacts** | Browse, preview, and optionally share files produced by job runs |
| **Observability** | Review job and chain runs across projects |
| **Cluster** | Check nodes and add workers |
| **Wiki** | Read help for the portal |

The menu only shows areas your role can use. An administrator can rename or reorder it under
**Settings → Menu**. If an item described in this wiki is missing, ask a workspace administrator
to check your permissions.

## Switch between light and dark mode

Use the sun or moon button beside **Sign out** at the bottom of the main menu. The choice takes
effect immediately and is remembered by this browser. It does not change the view for other users.

Workspace branding still applies in both modes. Custom dark background, panel, and text colours
are used in dark mode; light mode uses its own readable light palette with the workspace accent.

## A useful working loop

Create or update code, record the change through the agent, run a job, inspect its output, and
map useful results into project tables. Use chains, schedules, and events when the workflow
should run without manual steps.
