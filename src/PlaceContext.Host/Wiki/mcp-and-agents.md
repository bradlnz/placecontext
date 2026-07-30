# MCP and agents

*Connect an MCP client to PlaceContext.*

## Connect

The workspace MCP endpoint is shown on **Onboarding**. For a local install it is:

```text
http://localhost:7700/mcp
```

For Claude Code:

```bash
claude mcp add --transport http placecontext http://localhost:7700/mcp
```

The first protected call opens the browser sign-in flow. Access tokens are scoped to the signed-in
user, workspace, and permissions.

## Common agent workflows

An MCP agent can:

- onboard projects and read their overview;
- record activity and decisions;
- create, upload, and run jobs;
- build and run job chains;
- create schedules and event triggers;
- query project data with read-only SQL;
- save analytics charts;
- inspect run history and artifacts;
- search prior run output when embeddings are configured.

Ask the agent to call `job_authoring_guide` before writing a job. The `setup_hermes` tool can add
a reusable job-orchestration guide to a project.

## Keep access safe

Store secrets in the project Vault, not in prompts or source code. MCP tools enforce the caller's
permissions and tenant boundary. Reconnect the MCP client if an old authorization no longer
matches the user's current role.
