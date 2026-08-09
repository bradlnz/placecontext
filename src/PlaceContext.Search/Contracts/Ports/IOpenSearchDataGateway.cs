using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

/// <summary>Constrained server-side access to OpenSearch search and aggregation APIs.</summary>
public interface IOpenSearchDataGateway
{
    Task<IReadOnlyList<OpenSearchIndexView>> ListIndicesAsync(
        Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<OpenSearchFieldView>> ListFieldsAsync(
        Guid projectId, string indexPattern, CancellationToken ct = default);
    Task<OpenSearchLastUpdatedView> GetLastUpdatedAsync(
        Guid projectId, string indexPattern, IReadOnlyList<string> candidateFields,
        CancellationToken ct = default);
    Task<OpenSearchSearchView> SearchAsync(
        OpenSearchSearchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Create an index with a fixed mapping — the destination for a materialised project table.
    /// Field names are used verbatim; types come from the Postgres→OpenSearch mapping.
    /// </summary>
    Task CreateIndexAsync(
        Guid projectId, string indexName, IReadOnlyList<OpenSearchMappingField> mappingFields,
        CancellationToken ct = default);

    /// <summary>
    /// Bulk-index <paramref name="rows"/> (column-aligned to <paramref name="columnNames"/>) into
    /// <paramref name="indexName"/> as one JSON document per row, chunked internally. Values are
    /// sent as JSON strings; OpenSearch coerces them onto the index mapping. Columns named in
    /// <paramref name="jsonColumnNames"/> are emitted as raw JSON instead (for <c>object</c>-mapped
    /// jsonb columns). Returns the number of documents indexed, throwing if any was rejected.
    /// </summary>
    Task<int> IndexBulkAsync(
        Guid projectId, string indexName, IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default,
        IReadOnlyList<string>? jsonColumnNames = null);

    /// <summary>Delete an index; a missing index is not an error (idempotent re-materialise).</summary>
    Task DeleteIndexAsync(Guid projectId, string indexName, CancellationToken ct = default);

    /// <summary>Run a SELECT-style query through OpenSearch's SQL engine against the project's indices.</summary>
    Task<OpenSearchSqlResult> SearchSqlAsync(
        Guid projectId, string sql, CancellationToken ct = default);
}
