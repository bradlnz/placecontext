# Agent Chat

*Ask questions about the selected project and let the agent use project tools.*

## Start a chat

Select a project, then open **Chat**. Start with a suggested prompt or type your own question.
The agent can inspect project context, recent runs, artifacts, tables, and the dependency graph.

Responses stream into the page. Tool calls and their results remain visible with the message.
Graphs, maps, and supported artifacts can render inline.

## Sessions and files

The side panel lists saved sessions, recent artifacts, fetched data, graph context, and tool
history. You can create, clear, reopen, or delete sessions.

Attach or drag in CSV, JSON, text, PDF, DOCX, or XLSX files when the agent needs their contents.
The file appears as a tile in the conversation and its readable text is supplied to the agent.

## Agent settings

Use **Chat → Settings** to configure:

- the agent persona and prompt;
- retrieval-augmented context (RAG);
- temperature and response length;
- slash commands that call tools.

If the header says **No model configured**, an operator must configure a chat gateway before
responses can be generated.
