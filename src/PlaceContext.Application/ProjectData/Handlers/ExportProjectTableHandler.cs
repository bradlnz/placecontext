using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ExportProjectTableHandler : IQueryHandler<ExportProjectTableQuery, string>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    public ExportProjectTableHandler(IProjectRepository projects, IProjectDataStore store) { _projects = projects; _store = store; }

    public async Task<string> HandleAsync(ExportProjectTableQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, q.ProjectId, ct);
        return await _store.ExportTableCsvAsync(q.ProjectId, q.TableName, ct);
    }
}
