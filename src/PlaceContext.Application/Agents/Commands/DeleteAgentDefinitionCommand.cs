using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record DeleteAgentDefinitionCommand(Guid AgentId) : ICommand<bool>;
