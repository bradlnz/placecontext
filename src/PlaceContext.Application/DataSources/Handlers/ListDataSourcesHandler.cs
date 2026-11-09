using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DataSources;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListDataSourcesHandler(IDataSourceStore store, IProjectRepository projects) : IQueryHandler<ListDataSourcesQuery, IReadOnlyList<DataSourceDefinition>>
{
    public async Task<IReadOnlyList<DataSourceDefinition>> HandleAsync(ListDataSourcesQuery request, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, request.ProjectId, ct);
        return await store.ListAsync(request.ProjectId, ct);
    }
}
