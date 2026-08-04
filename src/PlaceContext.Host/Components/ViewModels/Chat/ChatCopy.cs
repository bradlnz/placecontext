using PlaceContext.Domain.Entities;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>User- and model-facing copy for the chat page — no inline string literals in the VM.</summary>
public static class ChatCopy
{
    public const string DefaultSessionTitle = "New Chat";
    public const string AttachmentsBucket = "chat-attachments";

    public const string GatewayActive = "Chat backend active";
    public const string GatewayUnconfigured = "No model configured";
    public const string ClusterStarting = "Cluster is starting — please try again in a moment.";
    public const string EmptyModelResponse =
        "The model returned an empty response. This might be due to the conversation context. Please try rephrasing your question.";

    public const string DefaultSystemPrompt =
        "You are a PlaceContext agent — a casual, friendly Australian mate who helps with project data, job runs, graphs, and artifacts. "
        + "Talk like a real Aussie: use words like 'mate', 'no worries', 'righto', 'cheers', 'good on ya', 'sweet as', 'no drama'. "
        + "Keep it relaxed and warm. CRITICAL: NEVER think out loud. NEVER write your thought process, reasoning, self-correction, or commentary about the conversation. "
        + "NEVER output phrases like 'Looking at the conversation', 'Let me think', 'I notice', 'Actually', 'Re-reading', or 'Hmm'. "
        + "NEVER wrap your answer in <think>, <reasoning>, or <reflection> tags. "
        + "If you catch yourself starting to explain your reasoning, STOP and give the answer directly. "
        + "When data is needed, call the right tool immediately without explaining why. "
        + "Keep answers short unless the user asks for detail. Never use formal corporate language — you're a mate, not a robot.";

    public static string DefaultPreamble => AgentConfig.DefaultPreamble;
    public static string DefaultToolCatalog => AgentConfig.DefaultToolCatalog;
    public static string DefaultLaunchpadToolCatalog => AgentConfig.DefaultLaunchpadToolCatalog;

    public const string NoProjectSelected = "No project selected";
    public const string ArtifactNotFound = "Artifact not found.";
    public const string LoadingTables = "Loading tables...";
    public const string LoadingJobs = "Loading jobs...";
    public const string LoadingChains = "Loading job chains...";
    public const string RenderingMap = "Rendering map...";
    public const string MapRendered = "Map rendered";
    public const string NoArtifactsYet = "No artifacts found for this project yet.";

    public static string QueryingTable(string tableName) => $"Querying {tableName}...";

    public static string LoadingArtifact(string shortId) => $"Loading artifact {shortId}...";

    public static string SearchingArtifacts(string query) =>
        $"Searching artifacts for \"{query}\"...";

    public static string LoadingArtifacts() => "Loading artifacts...";

    public static string ArtifactsMatchedNone(string query) => $"No artifacts matched \"{query}\".";

    public static string ArtifactsCount(int count) => $"{count} artifacts";

    public static string RunningJob(string shortId) => $"Running job {shortId}...";

    public static string RunningChain(string shortId) => $"Running chain {shortId}...";

    public static string CallingMcp(string server, string tool) => $"Calling {server}.{tool}...";

    public static string McpServerNotFound(string server, string available) =>
        $"MCP server '{server}' not found. Available: {available}";

    public static string McpCallFailed(string message) => $"MCP call failed: {message}";

    public static string UnknownTool(string name) => $"Unknown tool: {name}";

    public static string InvalidRenderMapJson => "Invalid JSON spec for render_map";
    public static string RenderMapUsage => "Usage: render_map|specJson";
}
