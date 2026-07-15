using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ImportCsvToProjectTableHandler : ICommandHandler<ImportCsvToProjectTableCommand, int>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;

    public ImportCsvToProjectTableHandler(IProjectRepository projects, IProjectDataStore store)
    {
        _projects = projects;
        _store = store;
    }

    public async Task<int> HandleAsync(ImportCsvToProjectTableCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        return await _store.ImportRowsAsync(c.ProjectId, c.TableName, c.Columns, c.Rows, c.CreateTable, ct);
    }
}
