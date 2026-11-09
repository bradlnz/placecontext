using PlaceContext.Application.Cqrs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListDataEntitiesHandler : IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>
{
    private readonly IDataEntityRepository _entities;

    public ListDataEntitiesHandler(IDataEntityRepository entities) => _entities = entities;

    public async Task<IReadOnlyList<DataEntityView>> HandleAsync(ListDataEntitiesQuery query, CancellationToken ct = default)
        => (await _entities.ListForProjectAsync(query.ProjectId, ct)).Select(DataEntityMapper.ToView).ToList();
}
