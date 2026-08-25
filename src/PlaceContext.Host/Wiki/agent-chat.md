# Agent Chat

*Ask questions about the selected project and let the agent use project tools.*

## Start a chat

Select a project, then open **Agent chat**. Each project owns an agent team and its own shared
channels. Start with a suggested prompt or type your own question.
The agent can inspect project context, recent runs, artifacts, tables, and the dependency graph.

Responses stream into the page. Tool calls and their results remain visible with the message.
Graphs, maps, and supported artifacts can render inline.

## Channels, team, and goals

The Slack-style side panel lists the configured agent team, shared channels, and the team's recent
goals. Use **+ Channel** to name and create a channel immediately; empty channels are saved and are
available to everyone working in that project. You can clear, reopen, or delete channels, and jump
from **Team goals** to the full work board.

The panel also keeps recent artifacts, fetched data, graph context, and tool history close to the
conversation.

Attach or drag in CSV, JSON, text, PDF, DOCX, or XLSX files when the agent needs their contents.
The file appears as a tile in the conversation and its readable text is supplied to the agent.

## Agent settings

Default administrators use **Settings → Agents** to select a project and configure:

- shared group-chat channels, including creating, opening, and deleting them;
- the enabled agent-team roster and a shortcut to the team and goals board;
- whether agent chat is enabled;
- the local base model and shared team instructions;
- retrieval context, temperature, and Top P;
- the interactive and unattended tool catalogs.

Use **Agents** in the main menu to add worker agents, assign capabilities, and manage the team goal
board. The Command Agent routes each channel request to the relevant enabled workers.

If the header says **No model configured**, an operator must configure a chat gateway before
responses can be generated.
