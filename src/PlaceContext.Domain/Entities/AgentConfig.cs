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

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string BaseModel { get; private set; }
    public string SystemPrompt { get; private set; }
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
            "You are a helpful assistant for this project. Use the provided context to answer questions accurately. Answer directly and concisely: no preamble, no restating the question, no visible chain-of-thought, no reasoning steps, no self-talk. Never output phrases like 'Looking at the conversation', 'Let me think', or 'I notice'. Provide the answer immediately. Keep answers short unless the user asks for detail.",
            DefaultMaxContextChunks, DefaultTemperature, DefaultTopP,
            enabled: true, now, now);
    }

    /// <summary>Rehydrates from persistence. Infrastructure only.</summary>
    public static AgentConfig Rehydrate(
        Guid id, Guid projectId, string baseModel, string systemPrompt,
        int maxContextChunks, float temperature, float topP, bool enabled,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, baseModel, systemPrompt, maxContextChunks, temperature, topP, enabled, createdAt, updatedAt);

    /// <summary>Updates the agent's configuration.</summary>
    public void Update(
        string baseModel, string systemPrompt, int maxContextChunks,
        float temperature, float topP, bool enabled, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(baseModel))
            throw new ArgumentException("Base model must not be empty.", nameof(baseModel));
        if (maxContextChunks < 1)
            throw new ArgumentOutOfRangeException(nameof(maxContextChunks), "Max context chunks must be >= 1.");

        BaseModel = baseModel.Trim();
        SystemPrompt = systemPrompt ?? "";
        MaxContextChunks = maxContextChunks;
        Temperature = Math.Clamp(temperature, 0f, 2f);
        TopP = Math.Clamp(topP, 0f, 1f);
        Enabled = enabled;
        UpdatedAt = updatedAt;
    }
}
