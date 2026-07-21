using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Updates a project's agent configuration (model, prompt, context settings).</summary>
public sealed record UpdateAgentConfigCommand(
    Guid ProjectId,
    string BaseModel,
    string SystemPrompt,
    int MaxContextChunks,
    float Temperature,
    float TopP,
    bool Enabled) : ICommand<AgentConfigView>;
