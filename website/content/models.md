# Connect your models

Configure named model connections, then select the model that fits each conversation or workflow task.

## Add a connection

As the default workspace administrator, open **Settings → Models → Add connection**.

1. Select a provider preset or enter a connection name, protocol, and API base URL.
2. Supply the provider's API key, or leave it empty for a local server that needs no authentication.
3. Use **Discover models**, or enter model IDs manually.
4. Review the available models, enable the connection, and save.

Choose a model with function/tool calling for agent work. Model discovery confirms the list endpoint responds; it does not prove every returned model supports chat, tools, or images.

## Supported connection paths

| Connection | How it is used |
| --- | --- |
| OpenAI | OpenAI-compatible Chat Completions |
| Anthropic | Anthropic Messages |
| Google Gemini | Its OpenAI-compatible endpoint |
| OpenRouter | OpenAI-compatible endpoint |
| Ollama / LM Studio | Externally managed compatible model server |
| Custom endpoint | A service implementing a supported protocol |
| Codex / Claude Code | Host-side CLI login, restricted to the default administrator |

Native Bedrock and Vertex adapters and Azure-specific authentication are not included in these paths. A provider's model features and usage limits still determine what a connection can do.

## Local servers

Local URLs are resolved from the PlaceContext application host. In a container deployment, `localhost` refers to that container. Configure a reachable address and the API base path expected by the provider.

The installer does not include a local inference worker. Run local model servers separately.

## CLI connections

Sign in on the app host using `codex login` or `claude auth login`, add the matching preset, and discover models. These connections use the host account's allowance and are available only to the default workspace administrator.

CLI sessions use private temporary directories. Their host tools, MCP servers, skills, hooks, and plugins are disabled; requested actions go through PlaceContext's tool validation and permissions.

## Choose models deliberately

A saved conversation retains its selected model. Disabled or removed connections require choosing another model. Configure automatic workflow agent steps independently in the workflow editor.

Keys are encrypted and are not returned to the browser or model. Changing an endpoint or protocol requires re-entering or explicitly removing its key.
