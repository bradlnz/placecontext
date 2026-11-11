# Troubleshooting

Start with the recorded error, the selected project, and the configuration of the capability being used.

## A model does not appear

Check that the connection is enabled, that model IDs are saved, and that your user can use the connection. Host-side Codex and Claude Code connections are restricted to the default workspace administrator. Discovering models does not test tool support.

## A local model cannot be reached

Resolve the endpoint from the application host or container. The browser's `localhost` is not the server's `localhost`. Check the API base URL and protocol, then test with a supported tool-capable model.

## The assistant is still working after I leave

Chat work runs on the server. Reopen the conversation from history or background-task notifications. Use **Stop** to request cancellation. Completed actions remain applied, and an interrupted action may need inspection before retrying.

## A workflow is waiting

Open the run and inspect the active step. **Needs input** indicates a human or external-agent response is required. A scheduled wait is different from a failed step. Respond using an account with the appropriate workflow permissions.

## A No answer did not stop later work

A No response records `false`. It does not cancel the workflow. Put `eq:approved:true` on every downstream action that requires approval, and review where the true and false paths rejoin.

## A query is rejected

Workflow queries must be read-only and complete. Narrow the selection, use unique column names, or aggregate the data if the result exceeds limits or would be truncated. Check `data.read` permission and the selected project's tables.

## A browser task cannot start

Confirm that the QEMU image exists at the configured host/container path and that the task's model and verification service are ready. Enabled-by-default execution does not supply a desktop image. Review the [browser setup guide](/docs/browser/).

## An MCP connection does not expose tools

Check that the connection is enabled, assigned to the selected agent, and belongs to the current project. Verify credentials and the server's HTTP JSON-RPC behavior. Consult [MCP integration limits](/docs/mcp/#verify-server-compatibility).

## An artifact cannot be opened

Confirm that artifact storage is configured, the run produced a file, and your user has access. Shared links can expire or be revoked. A successful job with primary JSON output does not necessarily produce a separate downloadable artifact.

## Get help with evidence

Use the [GitHub issue tracker](https://github.com/bradlnz/placecontext/issues) for reproducible product issues. Include the installed version, steps, expected and actual behavior, and sanitized errors. Keep credentials and private records out of reports. Use the [security policy](https://github.com/bradlnz/placecontext/blob/main/SECURITY.md) for security reports.
