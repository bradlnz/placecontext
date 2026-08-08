using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListCrmClientArtifactsHandler
    : IQueryHandler<ListCrmClientArtifactsQuery, IReadOnlyList<CrmClientArtifactView>>
{
    private readonly ICrmClientArtifactRepository _artifacts;

    public ListCrmClientArtifactsHandler(ICrmClientArtifactRepository artifacts)
        => _artifacts = artifacts;

    public async Task<IReadOnlyList<CrmClientArtifactView>> HandleAsync(
        ListCrmClientArtifactsQuery query,
        CancellationToken ct = default)
        => (await _artifacts.ListForClientAsync(query.ClientId, query.Take, ct))
            .Select(CrmClientArtifactMapper.ToView)
            .ToList();
}
