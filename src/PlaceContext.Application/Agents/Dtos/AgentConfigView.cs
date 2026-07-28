namespace PlaceContext.Application.Dtos;

/// <summary>Read model for a project's agent configuration.</summary>
public sealed record AgentConfigView(
    Guid Id,
    Guid ProjectId,
    string BaseModel,
    string SystemPrompt,
    string Preamble,
    string ToolCatalog,
    string LaunchpadToolCatalog,
    int MaxContextChunks,
    float Temperature,
    float TopP,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
