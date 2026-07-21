using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns the agent configuration for a project (creates a default one if none exists).</summary>
public sealed record GetAgentConfigQuery(Guid ProjectId) : IQuery<AgentConfigView>;
