# Projects

*The container for a codebase, its work, and its data.*

## Create a project

Projects register through MCP. Connect an MCP client from **Onboarding**, then ask it to onboard the
current repository. The `onboard` tool registers the path, reads available project guidance,
and imports useful context from git history.

The **Projects overview** shows every registered project and the workspace's current focus.

## What belongs to a project

A project owns:

- activity, decisions, and a dependency graph;
- jobs, chains, schedules, and events;
- SQL tables, mappings, analytics, and entities;
- encrypted vault secrets;
- run history and artifacts.

Select a project from the project switcher before using project pages.

## Project overview

The overview page shows status, recent activity, recorded decisions, graph statistics, and
high-degree files. MCP clients can keep it current with `record_activity`, `add_decision`, and
`rebuild_graph`.

Projects and workspaces are tenant-scoped. Users only see the areas allowed by their effective
permissions.

For a plain-language explanation of protected customer information, public file links, and access
codes, see [Security, privacy, and sharing](/wiki/security-and-sharing).
