using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class MaterializeTableIndexHandler : ICommandHandler<MaterializeTableIndexCommand, MaterializeTableIndexResult>
{
    /// <summary>Rows materialized per invocation — a safety valve on table size; result flags truncation.</summary>
    private const long MaxRows = 10000;

    private readonly IProjectRepository _projects;
    private readonly IOpenSearchDataGateway _openSearch;
    private readonly IProjectDataStore _store;

    public MaterializeTableIndexHandler(
        IProjectRepository projects,
        IOpenSearchDataGateway openSearch,
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
            new OpenSearchMappingField(name, MaterializeTableIndexCommand.OpenSearchTypeFor(read.ColumnTypes[i])))
            .ToList();
        var jsonbColumns = read.ColumnTypes
            .Select((t, i) => (t, i))
            .Where(p => p.t is "jsonb" or "json")
            .Select(p => read.Columns[p.i])
            .ToList();

        // Replace any existing index with the same name, then rebuild from the current table state.
        await _openSearch.DeleteIndexAsync(c.ProjectId, indexName, ct);
        await _openSearch.CreateIndexAsync(c.ProjectId, indexName, mappingFields, ct);
        var indexed = await _openSearch.IndexBulkAsync(
            c.ProjectId, indexName, read.Columns, read.Rows, ct, jsonbColumns);

        return new MaterializeTableIndexResult(
            indexName, indexed, mappingFields.Count, read.Truncated, c.TableName);
    }
}
