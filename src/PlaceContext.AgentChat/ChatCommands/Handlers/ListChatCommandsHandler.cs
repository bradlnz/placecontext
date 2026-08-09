using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListChatCommandsHandler : IQueryHandler<ListChatCommandsQuery, IReadOnlyList<ChatCommandView>>
{
    private readonly IChatCommandRepository _repo;

    public ListChatCommandsHandler(IChatCommandRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ChatCommandView>> HandleAsync(ListChatCommandsQuery query, CancellationToken ct = default)
    {
        var entities = await _repo.ListForProjectAsync(query.ProjectId, ct);
        return entities.Select(ChatCommandMapper.ToView).ToList();
    }
}
