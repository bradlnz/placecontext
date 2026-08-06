using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class MaterializeIndexTableHandler : ICommandHandler<MaterializeIndexTableCommand, MaterializeIndexResult>
{
    /// <summary>Rows materialized per invocation — a safety valve on index size; result flags truncation.</summary>
    private const int MaxRows = 10000;

    private readonly IProjectRepository _projects;
    private readonly IOpenSearchDataGateway _openSearch;
    private readonly IProjectDataStore _store;
    private readonly RecordLinkService? _links;
    private readonly ILogger<MaterializeIndexTableHandler>? _log;

    public MaterializeIndexTableHandler(
        IProjectRepository projects,
        IOpenSearchDataGateway openSearch,
        IProjectDataStore store,
        RecordLinkService? links = null,
        ILogger<MaterializeIndexTableHandler>? log = null)
    {
        _projects = projects;
        _openSearch = openSearch;
        _store = store;
        _links = links;
        _log = log;
    }

    public async Task<MaterializeIndexResult> HandleAsync(
        MaterializeIndexTableCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);

        var export = await _openSearch.ExportIndexAsync(c.ProjectId, c.IndexPattern, MaxRows, ct);
        if (export.Fields.Count == 0)
            throw new InvalidOperationException(
                $"Index '{c.IndexPattern}' has no queryable fields to materialize.");

        var tableName = string.IsNullOrWhiteSpace(c.TableName)
            ? MaterializeIndexTableCommand.DefaultTableName(c.IndexPattern)
            : c.TableName.Trim();

        var columns = DeduplicatedColumns(export.Fields);

        var tables = await _store.ListTablesAsync(c.ProjectId, ct);
        var existing = tables.FirstOrDefault(t =>
            t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.ReadOnly)
                throw new InvalidOperationException(
                    $"'{tableName}' is a read-only system table — pick a different table name.");
            if (existing.IsView)
                throw new InvalidOperationException(
                    $"'{tableName}' is a view — pick a different table name.");
        }

        if (existing is null)
        {
            await _store.ImportRowsAsync(c.ProjectId, tableName, columns, export.Rows, createTable: true, ct);
        }
        else
        {
            // ImportRowsAsync appends when the table already exists, so a plain re-run would
            // duplicate rows. Import into a staging name first — a failed import then leaves the
            // existing table untouched — and swap in with one drop + rename.
            var staging = StagingName(tableName);
            if (tables.Any(t => t.Name.Equals(staging, StringComparison.OrdinalIgnoreCase)))
                await _store.DropTableAsync(c.ProjectId, staging, ct);
            await _store.ImportRowsAsync(c.ProjectId, staging, columns, export.Rows, createTable: true, ct);
            await _store.DropTableAsync(c.ProjectId, tableName, ct);
            await _store.RenameTableAsync(c.ProjectId, staging, tableName, ct);
        }

        await RecordLinkHook.RefreshAsync(_links, c.ProjectId, tableName, _log, ct);
        return new MaterializeIndexResult(tableName, export.Rows.Count, columns.Count, export.Truncated, c.IndexPattern);
    }

    // OpenSearch field names ("address.city", "@timestamp") are not valid Postgres identifiers and
    // distinct fields can collide after sanitising ("a.b" vs "a_b") — deduplicate with a suffix.
    private static IReadOnlyList<ProjectColumnSpec> DeduplicatedColumns(IReadOnlyList<OpenSearchExportField> fields)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = new List<ProjectColumnSpec>(fields.Count);
        foreach (var field in fields)
        {
            var name = MaterializeIndexTableCommand.ColumnName(field.Name);
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate))
                candidate = $"{name}_{suffix++}";
            columns.Add(new ProjectColumnSpec(candidate, field.PostgresType, false, false));
        }
        return columns;
    }

    private static string StagingName(string tableName) =>
        (tableName.Length <= 47 ? tableName : tableName[..47]) + "_materialize_stg";
}
