using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Dtos;

public sealed record AgentDefinitionView(
    Guid Id,
    Guid ProjectId,
    AgentKind Kind,
    string Name,
    string Description,
    string Instructions,
    string TemplateKey,
    IReadOnlyList<AgentCapability> Capabilities,
    IReadOnlyList<Guid> AllowedJobIds,
    bool Enabled,
    DateTimeOffset UpdatedAt);
