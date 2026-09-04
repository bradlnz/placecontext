using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ListDataEntitiesHandler : IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>
{
    private readonly IDataEntityRepository _entities;

    public ListDataEntitiesHandler(IDataEntityRepository entities) => _entities = entities;

    public async Task<IReadOnlyList<DataEntityView>> HandleAsync(ListDataEntitiesQuery query, CancellationToken ct = default)
        => (await _entities.ListForProjectAsync(query.ProjectId, ct)).Select(DataEntityMapper.ToView).ToList();
}
