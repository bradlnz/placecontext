namespace PlaceContext.AgentChat.Contracts.Api;

public sealed record UpdateChatSettingsRequest(
    string? BaseModel,
    string? SystemPrompt,
    string? Preamble,
    string? ToolCatalog,
    string? LaunchpadToolCatalog,
    int MaxContextChunks,
    float Temperature,
    float TopP,
    bool Enabled);
