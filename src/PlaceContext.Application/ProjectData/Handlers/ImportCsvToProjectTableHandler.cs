using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ImportCsvToProjectTableHandler : ICommandHandler<ImportCsvToProjectTableCommand, ImportCsvResult>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectDataStore _store;
    private readonly RecordLinkService? _links;
    private readonly ILogger<ImportCsvToProjectTableHandler>? _log;

    public ImportCsvToProjectTableHandler(IProjectRepository projects, IProjectDataStore store,
        RecordLinkService? links = null, ILogger<ImportCsvToProjectTableHandler>? log = null)
    {
        _projects = projects;
        _store = store;
        _links = links;
        _log = log;
    }

    public async Task<ImportCsvResult> HandleAsync(ImportCsvToProjectTableCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);
        var warnings = await FindDuplicatesAsync(c, ct);
        var imported = await _store.ImportRowsAsync(c.ProjectId, c.TableName, c.Columns, c.Rows, c.CreateTable, ct);
        await RecordLinkHook.RefreshAsync(_links, c.ProjectId, c.TableName, _log, ct);
        return new ImportCsvResult(imported, warnings);
    }

    // The duplicate check runs against the PRE-import table state (after the import every new row
    // would match itself); warn-only — a failed check never blocks the import.
    private async Task<IReadOnlyList<string>> FindDuplicatesAsync(ImportCsvToProjectTableCommand c, CancellationToken ct)
    {
        if (_links is null || c.Rows.Count == 0) return Array.Empty<string>();
        try
        {
            var rows = c.Rows
                .Select(r => (IReadOnlyDictionary<string, string?>)c.Columns
                    .Select((col, i) => (col.Name, Value: i < r.Count ? r[i] : null))
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase))
                .ToList();
            return await _links.FindDuplicatesAsync(c.ProjectId, c.TableName, rows, ct: ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Duplicate check for '{Table}' failed — the import continues.", c.TableName);
            return Array.Empty<string>();
        }
    }
}
