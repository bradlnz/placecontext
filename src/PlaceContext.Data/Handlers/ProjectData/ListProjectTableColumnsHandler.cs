using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListProjectTableColumnsHandler : IQueryHandler<ListProjectTableColumnsQuery, IReadOnlyList<ProjectColumnInfo>>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public ListProjectTableColumnsHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<IReadOnlyList<ProjectColumnInfo>> HandleAsync(ListProjectTableColumnsQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return await _store.ListColumnsAsync(q.ProjectId, q.TableName, ct);
    }
}
