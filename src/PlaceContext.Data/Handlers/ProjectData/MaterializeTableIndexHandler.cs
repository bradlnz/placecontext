using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Data.Integration;

namespace PlaceContext.Application.Features;

public sealed class MaterializeTableIndexHandler : ICommandHandler<MaterializeTableIndexCommand, MaterializeTableIndexResult>
{
    /// <summary>Rows materialized per invocation — a safety valve on table size; result flags truncation.</summary>
    private const long MaxRows = 10000;

    private readonly IProjectRepository _projects;
    private readonly IDataSearchClient _openSearch;
    private readonly IProjectDataStore _store;

    public MaterializeTableIndexHandler(
        IProjectRepository projects,
        IDataSearchClient openSearch,
        IProjectDataStore store)
    {
        _projects = projects;
        _openSearch = openSearch;
        _store = store;
    }

    public async Task<MaterializeTableIndexResult> HandleAsync(
        MaterializeTableIndexCommand c, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(_projects, c.ProjectId, ct);

        var read = await _store.ReadTableAsync(c.ProjectId, c.TableName, MaxRows, ct);
        if (read.Columns.Count == 0)
            throw new InvalidOperationException(
                $"Table '{c.TableName}' has no columns to materialize.");
        if (read.TotalCount == 0)
            throw new InvalidOperationException(
                $"Table '{c.TableName}' has no rows to materialize.");

        var indexName = string.IsNullOrWhiteSpace(c.IndexName)
            ? MaterializeTableIndexCommand.DefaultIndexName(c.TableName)
            : c.IndexName.Trim();

        var mappingFields = read.Columns.Select((name, i) =>
            new DataSearchMappingField(name, MaterializeTableIndexCommand.OpenSearchTypeFor(read.ColumnTypes[i])))
            .ToList();
        var jsonbColumns = read.ColumnTypes
            .Select((t, i) => (t, i))
            .Where(p => p.t is "jsonb" or "json")
            .Select(p => read.Columns[p.i])
            .ToList();

        // Search owns OpenSearch credentials and replaces the index atomically from Data's bounded payload.
        await _openSearch.ReplaceIndexAsync(
            c.ProjectId, indexName, mappingFields, read.Columns, read.Rows, jsonbColumns, ct);
        var indexed = read.Rows.Count;

        return new MaterializeTableIndexResult(
            indexName, indexed, mappingFields.Count, read.Truncated, c.TableName);
    }
}
