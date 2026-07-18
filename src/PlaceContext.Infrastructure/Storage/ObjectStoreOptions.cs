namespace PlaceContext.Infrastructure.Storage;

/// <summary>
/// Options for <see cref="MinioObjectStore"/>, bound from "PlaceContext:ObjectStore". In-cluster these
/// point at the MinIO service; credentials come from the placecontext-minio secret (same as CNPG).
/// </summary>
public sealed class ObjectStoreOptions
{
    /// <summary>S3 endpoint, e.g. http://minio:9000. Empty disables the store (artifacts not written).</summary>
    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    /// <summary>Bucket post-job artifacts (reports/charts/CSVs/bundles) are written to.</summary>
    public string ReportsBucket { get; set; } = "placecontext-reports";

    /// <summary>Bucket for baked job-dependency layers (the Kubernetes runner's warm cache).</summary>
    public string DepsBucket { get; set; } = "placecontext-deps";
}
