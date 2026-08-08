using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListCrmClientCommunicationsHandler
    : IQueryHandler<ListCrmClientCommunicationsQuery, IReadOnlyList<CrmCommunicationView>>
{
    private readonly ICrmCommunicationRepository _communications;

    public ListCrmClientCommunicationsHandler(ICrmCommunicationRepository communications)
        => _communications = communications;

    public async Task<IReadOnlyList<CrmCommunicationView>> HandleAsync(
        ListCrmClientCommunicationsQuery query,
        CancellationToken ct = default)
        => (await _communications.ListForClientAsync(query.ClientId, query.Take, ct))
            .Select(CrmCommunicationMapper.ToView)
            .ToList();
}
