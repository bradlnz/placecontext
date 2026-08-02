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
