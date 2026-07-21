using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns a single chat session with its full message history.</summary>
public sealed record GetAgentChatSessionQuery(Guid SessionId) : IQuery<AgentChatSessionView?>;
