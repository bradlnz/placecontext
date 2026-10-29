# Build chat and model connections

Select a project and open **Build**. The assistant can inspect, create, replace, delete, and run jobs and workflows, and create, update, pause, or delete job schedules and event triggers. It uses your permissions and checks that referenced resources belong to the selected project. Existing job and workflow validation still applies.

Build can also deploy apps and workers into **Services**, inspect their source/status/logs, update source apps, and provision app-owned PostgreSQL or Redis databases. It defaults to the built-in hosting environment rather than asking for an external provider. Hosting tools require Settings management permission. Apps retain platform SSO, namespace isolation and non-root containers; database credentials are generated and injected automatically.

Source deployments support Python and Node with up to 100 files / 400 KB. The entrypoint runs from `/work` and web apps listen on `0.0.0.0:$PORT`. Dependency manifests use the existing pip/npm setup and require explicit public egress in the source definition; private networks remain blocked except the platform and owned databases. Source updates retain the app ID and database bindings. Larger apps use prebuilt images. `/work` and `/tmp` are ephemeral, so durable data belongs in the owned database. A deployment receipt means submitted, not ready; Build checks status and logs before reporting readiness.

## Connect models

Local development automatically probes Ollama at `127.0.0.1:11434/v1` and LM Studio at `127.0.0.1:1234/v1` when models are listed. Running servers with available models appear without API keys. The open chat refreshes discovery every 30 seconds. Detection does not install servers or download models. These addresses refer to the application host, not the browser machine. Production detection is off by default; set `PlaceContext:Build:DetectLocalModels` explicitly to override. Manually edited or disabled connections are preserved.

The default workspace administrator also gets automatic detection of **Codex** and **Claude Code** on the app host's `PATH`. Codex uses its signed-in account and the app-server model catalog. Claude Code checks its login and offers its configured `default` plus the `sonnet`, `opus`, and `haiku` aliases; availability depends on the account. Installed but signed-out CLIs appear with no models and a sign-in hint. Run `codex login` or `claude auth login` on the host, then refresh Build or discover models in Settings. The CLI must support the isolation flags used by this integration (validated with Codex 0.153.1 and Claude Code 2.1.259). Gemini and other CLI adapters are not implemented; compatible HTTP providers can still be configured manually.

CLI inference runs remotely and consumes the host account's usage allowance. Host CLI accounts are restricted to the default workspace administrator at both listing and execution. Credentials stay with the CLI. Runs use private temporary working directories; host tools, MCP servers, skills, hooks and plugins are disabled. Codex returns dynamic tool requests and retains its session through the tool loop; Claude returns structured requests with the conversation history and images supplied each call. Both go through the existing Build tool validation and permissions. Temporary sessions are removed after completion or cancellation.

New chats initially prefer enabled localhost or CLI connections with available models, falling back to another configured connection when none are available. Explicit model choices and saved conversations retain their selected model.

The default administrator opens **Settings → Models → Add connection**:

1. Choose a provider preset, or enter a custom connection name, protocol, and API base URL.
2. Enter the provider's API key. Leave it empty for a local server that requires no authentication.
3. Enter model IDs, one per line, or use **Save & discover models**. Review the discovered list and save the models users should be able to select.
4. Enable the connection. Project users can select its provider and model in the chat header.

Supported protocols are OpenAI-compatible Chat Completions and Anthropic Messages. Presets include OpenAI, Anthropic, Google Gemini's compatible endpoint, OpenRouter, Ollama, and LM Studio. Any custom service implementing these protocols can be configured. This follows LibreChat's named-endpoint, credentials, base-URL and configured/discovered-model pattern; it does not import `librechat.yaml` or claim support for every LibreChat adapter.

The URL is the API base (for example `https://api.anthropic.com/v1`), not the individual completion route. Local URLs resolve from the PlaceContext server, including its container network. Redirects are not followed. Changing a saved endpoint or protocol requires re-entering or explicitly removing its key. Saved keys are encrypted and are never returned to the browser or the model.

Use models with function/tool calling. Image input additionally requires vision support. Model discovery verifies the provider's model-list endpoint; it does not prove every returned model supports chat, tools, or images. API billing and quotas belong to the connected provider. Browser-based subscription sign-in, Azure-specific authentication, and native Bedrock/Vertex adapters are not included; local Codex and Claude Code reuse their own sign-ins.

## Conversations and memory

Conversations, image attachments, titles, and memory are encrypted in PostgreSQL using the existing Data Protection key ring. They are scoped to the signed-in user, tenant, and project. Switching models preserves the conversation. Disabled or deleted connections require selecting another model.

Project memory carries across conversations. Ask the assistant to remember a preference, or open **Project memory** to review, edit, or clear it. Do not put credentials in memory; use the vault. Deleting a chat deletes its attachments but keeps project memory and any jobs, workflows, or schedules created through it.

When the assistant asks a question with concrete choices, it can present clickable answers. Selecting one sends that answer and resumes the conversation; you can also type a custom reply. Buttons are disabled for earlier questions, while work is running, or while you have a draft, attachment, or active dictation. Questions and choices remain available after reopening the conversation.

The selected provider receives the conversation, attachments, saved memory, project dependency map, and tool results. Conversation history is sent in full, bounded by storage and message limits; model context limits can be lower. Start another chat when needed. There is no silent history truncation or automatic summarization.

## Inputs and execution

- Text: compose a message and select **Send** or press Enter; Shift+Enter creates a new line.
- Images: attach up to four PNG, JPEG, or WebP files, no larger than 2 MB each. Remove them before sending using the preview's remove button.
- Speech: **Dictate** fills the text composer; stop dictation and review the transcript before sending. It uses browser speech recognition, which may send audio to the browser vendor. A supported browser, microphone permission, and HTTPS (or localhost) are required. Text and image input remain available when speech is unsupported.
- Tool activity: expand actions and results to inspect what the assistant did. Replies appear after a model response completes; tool progress updates between calls.
- Stop: cancels further work. Completed changes remain applied. If interrupted during an action, its result may be unknown; inspect the resource before retrying.

Sending persists the user message and queues a server-owned run. Closing a tab, navigating away, starting another chat, or disconnecting does not cancel it. Reopening Build reconnects to an active conversation; History shows queued/running conversations and saved results. Build tasks appear in the background-task notifications for their submitting user. Selecting **view** opens that exact conversation in a side blade without navigating away. Closing the blade leaves the background run active. Stop persists a cancellation request. Workers use fresh scopes and reload the submitting user's identity and permissions. Up to three conversations run concurrently per application instance.

Queued work survives a server restart. Running work interrupted by a server shutdown stops; after a crash, an expired claim is marked Interrupted and unresolved tool calls receive an unknown-outcome result. It is never blindly replayed. Review results and send a follow-up to continue.

Each turn starts with a permission-filtered project map: job configurations, shared job IDs across workflows, stage order, parallel groups, waits, conditions, nested branches, action kinds, and schedule-to-job links. The map caps each resource list at 200 and reports totals; it includes three recent run summaries for the five most recently updated jobs and workflows. Detailed job source, complete workflows, run logs and artifacts remain available through tools. The agent refreshes context after changes and inspects contracts and downstream dependencies before optimization. Improvements require supporting evidence; autonomous recurring optimization is not scheduled implicitly.

A turn is limited to 20 model steps and 30 minutes. Each model response has a separate 10-minute limit. Provider response timeouts, the overall turn deadline, and external cancellation are reported separately; completed actions are preserved. Concurrent turns in the same conversation are blocked by a database lease. A live worker renews its lease every 10 seconds, including while waiting for a model response. If renewal fails, the run stops; a crashed server's lease expires after 60 seconds. There are no automatic retries of mutations.

## Deployment

Apply the `AddBuildChatAndModelConnections` and `AddBuildBackgroundRuns` EF migrations through the normal deployment/startup migration path. They add `model_connections`, `build_conversations`, and `build_memories`, plus background status/cancellation and local discovery fields. Existing agent tables and trigger columns are retained. Preserve the Data Protection key ring with database backups. These private chat/model tables are included in full PostgreSQL backups, not the portable project manifest export.

Protocol references: [Codex app-server](https://learn.chatgpt.com/docs/app-server), [Claude Code programmatic use](https://code.claude.com/docs/en/headless), [LibreChat custom endpoints](https://www.librechat.ai/docs/configuration/librechat_yaml/object_structure/custom_endpoint), [Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create), [Anthropic tool results](https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls), [Gemini compatibility](https://ai.google.dev/gemini-api/docs/openai/).
