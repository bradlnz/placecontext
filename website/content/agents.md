# Work with agents and chat

Use the dashboard assistant to explore project context, build workflows, inspect results, and take action through project tools. Agents work with the permissions of the signed-in user.

## Start from the dashboard

Open a project and choose **Dashboard**. Describe the outcome you want, or use **Run a workflow** to open the workflow picker immediately. Selecting a workflow previews it; **Prepare run in chat** lets you supply inputs and follow the run.

Opening the picker does not require an AI request and does not start a run.

## Configure a named agent

Open **Agent → Instructions**. Create a named agent, select its model connection and model, write instructions, and assign the project's enabled MCP connections. Choose the agent in the chat header.

Agent profiles are private to you within the project. Shared project instructions in this panel apply across your agent profiles; they do not grant other users access or expand your permissions. Model and connection credentials stay server-side.

## Inspect context and tools

In **Agent → Context & tools**, inspect saved project memory and the available tool schemas. The assistant starts with a permission-filtered map of project jobs, workflows, schedules, and recent run summaries. It retrieves detailed records and output through tools as needed.

The panel also exposes the current system prompt and the last recorded prompt and tool schemas used by the conversation. It does not expose private model reasoning or vault credentials.

## Keep useful memory

Saved memory carries useful facts across your conversations in the same project. Ask the assistant to review, correct, or forget it, or edit the memory in the Agent panel. Deleting a conversation does not delete project memory or resources created during that conversation.

The selected model provider receives the conversation and the context used for that request. Keep credentials in the vault.

## Follow background work

Sending a message persists it and queues a server-owned run. Navigating away or closing the tab does not cancel that work. Reopen the conversation from history or the background-task notification to see its progress and results.

Replies appear after a model response completes, with tool progress between calls. **Stop** requests cancellation; actions already completed remain applied. If an interrupted action has an unknown outcome, inspect the resource before retrying.

New messages currently accept text. Browser dictation can fill the composer for review before sending. Provider context limits still apply to long conversations.

## Add agents to workflows

An automatic workflow agent has its own task, model, and bounded project job tools. It records activity and returns a JSON result for later stages. External-agent mode pauses for a fresh response. You can place a separate reviewer or human checkpoint after either.

[Configure models](/docs/models/) · [Compose decisions](/docs/decisions/) · [Execution details](https://github.com/bradlnz/placecontext/blob/main/docs/build-chat.md)
