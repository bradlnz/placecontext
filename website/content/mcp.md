# Connect tools through MCP

PlaceContext supports two directions: external AI clients can use PlaceContext's tools, and a configured dashboard agent can use assigned project MCP connections.

## Connect an AI client to PlaceContext

The workspace MCP endpoint is the workspace URL followed by `/mcp`. For a local installation:

```text
http://localhost:7700/mcp
```

For Codex:

```bash
codex mcp add placecontext --url http://localhost:7700/mcp
```

For Claude Code:

```bash
claude mcp add --transport http placecontext http://localhost:7700/mcp
```

Use your actual workspace URL for a remote installation. Compatible clients discover the OAuth flow and open browser sign-in. Tools run within the signed-in user's workspace and permission boundary.

## Work with project resources

An authorized client can inspect projects, record decisions, author and run jobs, build workflows, configure schedules, query project data, and inspect results. Call `job_authoring_guide` before writing a job.

For long-running service integrations, `submit_job_chain` returns a durable receipt. Store its tracking ID and poll `get_job_chain_submission` rather than holding a synchronous call open. Use a stable idempotency key for safe submission retries.

## Assign a connection to an agent

Configure a project connection under **Settings → MCP**. In the dashboard, open **Agent → Instructions**, select the agent, and assign the enabled connections it should use.

Only assigned, enabled connections in the current project are exposed to that agent. Current chat configuration uses this assignment panel; typing a mention is not a special tool-routing mechanism.

## Verify server compatibility

Outbound connections currently use HTTP JSON-RPC tool discovery and calls. Test the specific server before depending on it. The implementation does not establish universal compatibility with every MCP transport, session behavior, or extension.

Connection credentials belong to the configured project connection. OAuth connections and managed provider connections need their own setup; they should not be treated as per-user accounts merely because an agent uses them.

## Control external actions

Assign trusted connections and keep credentials in the vault. An MCP tool may change an external system. Use an explicit workflow human checkpoint and condition where authorization is required; the presence of a connected tool does not itself create an approval gate.

[API integrations](/docs/api/) · [Security and access](/docs/security/)
