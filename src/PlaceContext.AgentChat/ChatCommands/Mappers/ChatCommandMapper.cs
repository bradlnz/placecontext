using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class ChatCommandMapper
{
    internal static ChatCommandView ToView(ChatCommand c) => new(
        c.Id, c.ProjectId, c.Name, c.Description,
        c.ToolName, c.Args, c.CreatedAt, c.UpdatedAt);
}
