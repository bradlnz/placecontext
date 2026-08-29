# MCP and jobs

*Connect an MCP client to PlaceContext.*

## Connect

The workspace MCP endpoint is shown on **Onboarding**. For a local install it is:

```text
http://localhost:7700/mcp
```

Replace the local URL below with the workspace URL shown on **Onboarding** when PlaceContext runs
on another machine.

### Codex

```bash
codex mcp add placecontext --url http://localhost:7700/mcp
```

### Claude Code

```bash
claude mcp add --transport http placecontext http://localhost:7700/mcp
```

### Gemini Antigravity

Open **Agent → MCP Servers → Manage MCP Servers**, add a custom server named `placecontext`, and
set its URL to `http://localhost:7700/mcp`.

### Hermes Agent

```bash
hermes mcp add placecontext --url http://localhost:7700/mcp --auth oauth
hermes mcp test placecontext
```

The first protected call opens the browser sign-in flow. Access tokens are scoped to the signed-in
user, workspace, and permissions.

Once connected, try:

```text
Onboard this repository into PlaceContext, then show me its recent activity and available jobs.
```

## Common workflows

An MCP client can:

- onboard projects and read their overview;
- record activity and decisions;
- create, upload, and run jobs;
- build and run job chains;
- create schedules and event triggers;
- query project data with read-only SQL;
- save analytics charts;
- inspect run history and artifacts;
- search prior run output when embeddings are configured.

Call `job_authoring_guide` before writing a job. The `setup_hermes` tool can add a reusable
job-orchestration guide to a project.

## Keep access safe

Store secrets in the project Vault, not in prompts or source code. MCP tools enforce the caller's
permissions and tenant boundary. Reconnect the MCP client if an old authorization no longer matches
the user's current role.
