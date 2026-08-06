namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchIndexView(string Name, long DocumentCount, string? StoreSize);

public sealed record OpenSearchFieldView(
    string Name, string Type, bool Searchable, bool Aggregatable);

public sealed record OpenSearchLastUpdatedView(
    DateTimeOffset? Value, string? Field);

public sealed record OpenSearchSyncView(
    bool Accepted, string Status, string Message);

public sealed record OpenSearchHitView(
    string Index,
    string Id,
    double? Score,
    IReadOnlyDictionary<string, string?> Fields);

public sealed record OpenSearchSearchRequest(
    Guid ProjectId,
    string IndexPattern,
    string? QueryText,
    int Page = 1,
    int PageSize = 25,
    string? BucketField = null,
    string BucketType = "terms",
    string ChartType = "bar",
    string MetricType = "count",
    string? MetricField = null,
    string? DateInterval = null);

public sealed record OpenSearchSearchView(
    long Total,
    int TookMs,
    IReadOnlyList<OpenSearchHitView> Hits,
    string? ChartSpecJson);

/// <summary>One column of a materialised index: the field name and its OpenSearch mapping type.</summary>
public sealed record OpenSearchMappingField(string Name, string OpenSearchType);

public sealed record OpenSearchDashboardView(
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
