using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListCrmClientsHandler
    : IQueryHandler<ListCrmClientsQuery, IReadOnlyList<CrmClientView>>
{
    private readonly ICrmClientRepository _clients;

    public ListCrmClientsHandler(ICrmClientRepository clients) => _clients = clients;

    public async Task<IReadOnlyList<CrmClientView>> HandleAsync(
        ListCrmClientsQuery query,
        CancellationToken ct = default)
        => (await _clients.ListForProjectAsync(query.ProjectId, ct))
            .Select(CrmClientMapper.ToView)
            .ToList();
}
