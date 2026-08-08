using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Features;

internal static class AgentSessionViewMapper
{
    public static AgentChatSessionView ToView(AgentChatSession s) => new(
        s.Id, s.ProjectId, s.UserId, s.Title,
        s.Messages.Select(m => new AgentMessageView(m.Role, m.Content, m.Timestamp)).ToList(),
        s.CreatedAt, s.UpdatedAt);
}
