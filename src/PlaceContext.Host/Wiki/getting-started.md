# Getting started

*A quick guide to the PlaceContext portal.*

## What PlaceContext does

PlaceContext keeps project work, customer information, automated tasks, saved files, and agent
activity in one workspace.

You do not need to understand the technical parts to use the portal. A **job** is a saved task, a
**chain** joins several tasks into a workflow, and an **artifact** is a file produced by a task.

## First steps

1. Select a project from the project switcher in the lower-left corner.
2. Open **Jobs** to run a saved task, or **CRM** to work with customers.
3. Follow progress in **Jobs** or **Observability**.
4. Open **Artifacts** to view files produced by completed tasks.

If no projects exist yet, open **Onboarding**. It explains how to connect an approved AI or MCP
client that can register a code project for you.

## Main areas

| Area | Purpose |
|---|---|
| **Dashboard** | Workspace totals, recent runs, charts, and pinned entities |
| **CRM** | Manage clients, lifecycle stages, notes, messages, files, and automations |
| **Project overview** | Project activity, decisions, and dependency graph |
| **Jobs** | Create, edit, run, and monitor jobs |
| **Tests** | Check a job with saved example inputs before using it in a workflow |
| **Chains** | Build multi-stage job pipelines |
| **Schedules** | Manage scheduled, event, and launchpad triggers |
| **Data** | Tables, SQL analytics, data mappings, entities, and searchable project data |
| **Vault** | Store encrypted project secrets |
| **Events** | View event types and recent occurrences |
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
