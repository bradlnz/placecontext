using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

public sealed record OpenSearchConnection(
    string Endpoint,
    string? Username,
    string? Password,
    string DefaultIndexPattern);

/// <summary>Resolves the OpenSearch connection without exposing credentials to UI read models.</summary>
public interface IOpenSearchConnectionResolver
{
    Task<OpenSearchConnection?> ResolveAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetJobEnvironmentAsync(
        Guid projectId, CancellationToken ct = default);
}

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
    Task<ProjectQueryResult> SearchSqlAsync(
        Guid projectId, string sql, CancellationToken ct = default);
}

/// <summary>Triggers the external collector without coupling the application to its transport.</summary>
public interface IOpenSearchSyncGateway
{
    Task<OpenSearchSyncView> TriggerAsync(CancellationToken ct = default);
}

public sealed record OpenSearchDashboardRecord(
    Guid Id,
    Guid ProjectId,
    string Name,
    string IndexPattern,
    string? QueryText,
    string BucketField,
    string BucketType,
    string ChartType,
    string MetricType,
    string? MetricField,
    string? DateInterval,
    string ChartSpecJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IOpenSearchDashboardStore
{
    Task<IReadOnlyList<OpenSearchDashboardRecord>> ListAsync(
        Guid projectId, CancellationToken ct = default);
    Task<OpenSearchDashboardRecord?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(OpenSearchDashboardRecord item, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
