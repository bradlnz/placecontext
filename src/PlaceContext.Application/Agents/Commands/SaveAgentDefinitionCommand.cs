using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Features;

public sealed record SaveAgentDefinitionCommand(
    Guid ProjectId,
    Guid? AgentId,
    string Name,
    string Description,
    string Instructions,
    string TemplateKey,
    IReadOnlyList<AgentCapability> Capabilities,
    IReadOnlyList<Guid> AllowedJobIds,
    bool Enabled) : ICommand<AgentDefinitionView>;
