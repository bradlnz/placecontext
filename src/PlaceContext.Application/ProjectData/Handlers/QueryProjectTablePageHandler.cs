using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class QueryProjectTablePageHandler : IQueryHandler<QueryProjectTablePageQuery, ProjectTablePageResult>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public QueryProjectTablePageHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<ProjectTablePageResult> HandleAsync(QueryProjectTablePageQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return await _store.QueryTablePageAsync(q.ProjectId, q.TableName, q.Search, q.Page, q.PageSize, ct);
    }
}
