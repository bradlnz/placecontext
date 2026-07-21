using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetAgentChatSessionHandler : IQueryHandler<GetAgentChatSessionQuery, AgentChatSessionView?>
{
    private readonly IAgentChatSessionRepository _sessions;

    public GetAgentChatSessionHandler(IAgentChatSessionRepository sessions) => _sessions = sessions;

    public async Task<AgentChatSessionView?> HandleAsync(GetAgentChatSessionQuery query, CancellationToken ct = default)
    {
        var session = await _sessions.GetByIdAsync(query.SessionId, ct);
        return session is null ? null : AgentSessionViewMapper.ToView(session);
    }
}
