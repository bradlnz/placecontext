using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListProjectTableSchemasHandler(IProjectRepository projects, IProjectDataStore store)
    : IQueryHandler<ListProjectTableSchemasQuery, IReadOnlyList<ProjectTableSchema>>
{
    public async Task<IReadOnlyList<ProjectTableSchema>> HandleAsync(ListProjectTableSchemasQuery query, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, query.ProjectId, ct);
        return await store.ListTableSchemasAsync(query.ProjectId, ct);
    }
}
