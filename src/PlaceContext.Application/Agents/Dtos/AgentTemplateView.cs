using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Dtos;

public sealed record AgentTemplateView(
    string Key,
    string Name,
    string Description,
    string Instructions,
    IReadOnlyList<AgentCapability> Capabilities);
