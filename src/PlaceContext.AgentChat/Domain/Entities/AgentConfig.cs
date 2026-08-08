using PlaceContext.Domain.Common;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: project-scoped configuration for the chat agent. Exactly one per project.
/// Controls which model is used, the system prompt, context window, and whether the agent is enabled.
/// </summary>
public sealed class AgentConfig : AggregateRoot
{
    private AgentConfig(
        Guid id,
        Guid projectId,
        string baseModel,
        string systemPrompt,
        string preamble,
        string toolCatalog,
        string launchpadToolCatalog,
        int maxContextChunks,
        float temperature,
        float topP,
        bool enabled,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        BaseModel = baseModel;
        SystemPrompt = systemPrompt;
        Preamble = preamble;
        ToolCatalog = toolCatalog;
        LaunchpadToolCatalog = launchpadToolCatalog;
        MaxContextChunks = maxContextChunks;
        Temperature = temperature;
        TopP = topP;
        Enabled = enabled;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public const string DefaultBaseModel = "qwen3.5:0.8b";
    public const int DefaultMaxContextChunks = 5;
    public const float DefaultTemperature = 0.7f;
    public const float DefaultTopP = 0.9f;

    public const string DefaultPreamble =
        "CRITICAL: NEVER think out loud. NEVER write your thought process, reasoning, self-correction, or commentary about the conversation. " +
        "NEVER output text like 'Looking at the conversation', 'Let me think', 'I notice', 'Actually', 'Re-reading', or 'Hmm'. " +
        "NEVER wrap your answer in <think>, <reasoning>, or <reflection> tags. " +
        "If you catch yourself starting to explain your reasoning, STOP and give the answer directly. " +
        "You are a casual Australian mate. Talk like one: use 'mate', 'no worries', 'righto', 'cheers', 'sweet as'. " +
        "Provide only the final answer or the tool call. If a tool is needed, emit it immediately without explanation.\n\n";

    public const string DefaultToolCatalog =
        "Available tools (use [[tool:toolName|args]] syntax). " +
        "IMPORTANT: Always pass ALL known parameters (table names, column names, IDs, etc.) from the conversation context. " +
        "Do not ask the user for information you already have. " +
        "Tool routing: use get_artifacts for reports/files/artifacts, search only for run output text, query_table for table data.\n\n" +
        "Built-in tools:\n" +
        "- [[tool:list_tables|]] - List all project data tables\n" +
        "- [[tool:query_table|tableName|page]] - Query a table (pass table name from context)\n" +
        "- [[tool:list_jobs|]] - List all jobs\n" +
        "- [[tool:list_job_runs|jobId]] - List runs for a job\n" +
        "- [[tool:list_chains|]] - List all job chains\n" +
        "- [[tool:render_graph|chartType|tableName|columnName]] - Render a chart (pass table AND column names from context, e.g. [[tool:render_graph|bar|cashflow_runs|amount]])\n" +
        "- [[tool:query_graph|]] - Query project dependency graph\n" +
        "- [[tool:search|query]] - Semantic search over job run output text/logs only (not files/reports)\n" +
        "- [[tool:get_artifacts|query]] - Search project artifacts by title/kind. Returns METADATA ONLY: title, kind, size, and id. Does NOT return file content. Use this to find the artifact id, then ALWAYS call show_artifact to get the actual content. Do NOT summarize or describe artifact content based on get_artifacts results alone — you have not seen the content yet.\n" +
        "- [[tool:show_artifact|artifactId]] - Fetches and returns the ACTUAL CONTENT of an artifact (extracted text for docs, raw content for text files). You MUST call this after get_artifacts before summarizing, describing, or answering questions about artifact content. This is the only way to see what's inside an artifact.\n" +
        "- [[tool:schedule_job|jobId|name|cron]] - Create a cron schedule\n" +
        "- [[tool:list_schedules|jobId]] - List job schedules\n" +
        "- [[tool:toggle_schedule|triggerId|true|false]] - Enable/disable schedule\n" +
        "- [[tool:run_job|jobId]] - Run a job now\n" +
        "- [[tool:run_job_chain|chainIdOrName|payloadJson]] - Run a job chain now (id or exact name; payloadJson optional). Prefer list_chains first if unsure.\n" +
        "- [[tool:call_mcp|serverName|toolName|argsJson]] - Call a tool on an external MCP server\n" +
        "- [[tool:list_mcp_tools|serverName]] - List available tools on an MCP server\n" +
        "- [[tool:render_map|specJson]] - Render a Leaflet map (JSON spec with {markers:[{lat,lng,label,color}], polygons:[{coords,color}], center:[lat,lng], zoom}). Example: [[tool:render_map|{\\\"markers\\\":[{\\\"lat\\\":48.135,\\\"lng\\\":11.582,\\\"label\\\":\\\"Munich\\\"}]}]]";

    public const string DefaultLaunchpadToolCatalog =
        "Available tools (use [[tool:toolName|args]] syntax). " +
        "IMPORTANT: Always pass ALL known parameters (table names, column names, IDs, etc.) from the conversation context. " +
        "Do not ask the user for information you already have.\n\n" +
        "Built-in tools:\n" +
        "- [[tool:list_tables|]] - List all project data tables\n" +
        "- [[tool:query_table|tableName|page]] - Query a table (pass table name from context)\n" +
        "- [[tool:list_jobs|]] - List all jobs\n" +
        "- [[tool:list_job_runs|jobId]] - List runs for a job\n" +
        "- [[tool:list_chains|]] - List all job chains\n" +
        "- [[tool:run_job|jobId]] - Run a job now\n" +
        "- [[tool:run_job_chain|chainIdOrName|payloadJson]] - Run a job chain now (id or exact name; payloadJson optional). Prefer list_chains first if unsure.\n" +
        "- [[tool:search|query]] - Semantic search over project run outputs and data\n" +
        "- [[tool:query_graph|]] - Query project dependency graph\n" +
        "- [[tool:get_artifacts|]] - List recent project artifacts (reports, charts, CSVs) with download links\n" +
        "- [[tool:list_schedules|]] - List job schedules";

    public const string LaunchpadPostamble =
        "You are running unattended on a schedule. Never ask clarifying questions — " +
        "decide and act. When finished, reply with a brief summary of what you did.";

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string BaseModel { get; private set; }
    public string SystemPrompt { get; private set; }
    public string Preamble { get; private set; }
    public string ToolCatalog { get; private set; }
    public string LaunchpadToolCatalog { get; private set; }
    public int MaxContextChunks { get; private set; }
    public float Temperature { get; private set; }
    public float TopP { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory: creates a new agent config with defaults for a project.</summary>
    public static AgentConfig Create(Guid projectId, DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));

        return new AgentConfig(
            Guid.NewGuid(), projectId,
            DefaultBaseModel,
            "You are a casual, friendly Australian mate who helps with project data, job runs, graphs, and artifacts. Talk like a real Aussie: use words like 'mate', 'no worries', 'righto', 'cheers', 'good on ya', 'sweet as', 'no drama'. Keep it relaxed and warm. Use the provided context to answer questions accurately. CRITICAL: NEVER think out loud. NEVER write your thought process, reasoning, self-correction, or commentary about the conversation. NEVER output phrases like 'Looking at the conversation', 'Let me think', 'I notice', 'Actually', 'Re-reading', or 'Hmm'. NEVER wrap your answer in <think>, <reasoning>, or <reflection> tags. If you catch yourself starting to explain your reasoning, STOP and give the answer directly. Answer the user's request directly and concisely. If a tool is needed, emit the tool call immediately without explanation. Keep answers short unless the user asks for detail. Never use formal corporate language — you're a mate, not a robot.",
            DefaultPreamble,
            DefaultToolCatalog,
            DefaultLaunchpadToolCatalog,
            DefaultMaxContextChunks, DefaultTemperature, DefaultTopP,
            enabled: true, now, now);
    }

    /// <summary>Rehydrates from persistence. Infrastructure only.</summary>
    public static AgentConfig Rehydrate(
        Guid id, Guid projectId, string baseModel, string systemPrompt,
        string preamble, string toolCatalog, string launchpadToolCatalog,
        int maxContextChunks, float temperature, float topP, bool enabled,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, baseModel, systemPrompt, preamble, toolCatalog, launchpadToolCatalog,
            maxContextChunks, temperature, topP, enabled, createdAt, updatedAt);

    /// <summary>Updates the agent's configuration.</summary>
    public void Update(
        string baseModel, string systemPrompt, string preamble, string toolCatalog,
        string launchpadToolCatalog, int maxContextChunks,
        float temperature, float topP, bool enabled, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(baseModel))
            throw new ArgumentException("Base model must not be empty.", nameof(baseModel));
        if (maxContextChunks < 1)
            throw new ArgumentOutOfRangeException(nameof(maxContextChunks), "Max context chunks must be >= 1.");

        BaseModel = baseModel.Trim();
        SystemPrompt = systemPrompt ?? "";
        Preamble = preamble ?? "";
        ToolCatalog = toolCatalog ?? "";
        LaunchpadToolCatalog = launchpadToolCatalog ?? "";
        MaxContextChunks = maxContextChunks;
        Temperature = Math.Clamp(temperature, 0f, 2f);
        TopP = Math.Clamp(topP, 0f, 1f);
        Enabled = enabled;
        UpdatedAt = updatedAt;
    }
}
