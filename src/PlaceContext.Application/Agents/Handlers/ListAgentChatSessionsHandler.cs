using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListAgentChatSessionsHandler : IQueryHandler<ListAgentChatSessionsQuery, IReadOnlyList<AgentChatSessionView>>
{
    private readonly IAgentChatSessionRepository _sessions;

    public ListAgentChatSessionsHandler(IAgentChatSessionRepository sessions) => _sessions = sessions;

    public async Task<IReadOnlyList<AgentChatSessionView>> HandleAsync(ListAgentChatSessionsQuery query, CancellationToken ct = default)
    {
        var sessions = await _sessions.ListForProjectAsync(query.ProjectId, ct);
        return sessions.Select(AgentSessionViewMapper.ToView).ToList();
    }
}
