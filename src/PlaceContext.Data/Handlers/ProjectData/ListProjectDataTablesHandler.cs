using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ListProjectDataTablesHandler : IQueryHandler<ListProjectDataTablesQuery, IReadOnlyList<ProjectTableInfo>>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;

    public ListProjectDataTablesHandler(IProjectRepository projects, IProjectDataStore store)
    {
        _projects = projects;
        _store = store;
    }

    public async Task<IReadOnlyList<ProjectTableInfo>> HandleAsync(ListProjectDataTablesQuery query, CancellationToken ct = default)
    {
        _ = await _projects.GetByIdAsync(ProjectId.From(query.ProjectId), ct)
            ?? throw new InvalidOperationException($"Project {query.ProjectId} not found.");
        return await _store.ListTablesAsync(query.ProjectId, ct);
    }
}
