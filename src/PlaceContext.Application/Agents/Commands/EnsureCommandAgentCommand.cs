using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record EnsureCommandAgentCommand(Guid ProjectId) : ICommand<AgentDefinitionView>;
