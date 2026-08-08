using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns all chat sessions for a project (newest first).</summary>
public sealed record ListAgentChatSessionsQuery(Guid ProjectId) : IQuery<IReadOnlyList<AgentChatSessionView>>;
