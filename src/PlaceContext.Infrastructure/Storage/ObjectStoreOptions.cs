namespace PlaceContext.Infrastructure.Storage;

/// <summary>
/// Options for <see cref="MinioObjectStore"/>, bound from "PlaceContext:ObjectStore". In-cluster these
/// point at the MinIO service; credentials come from the placecontext-minio secret (same as CNPG).
/// Supports S3-compatible stores (MinIO, Digital Ocean Spaces, AWS S3).
/// </summary>
public sealed class ObjectStoreOptions
{
    /// <summary>S3 endpoint, e.g. http://minio:9000. Empty disables the store (artifacts not written).</summary>
    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";

    /// <summary>
    /// AWS region for S3/Digital Ocean Spaces (e.g. "us-east-1", "nyc3").
    /// Defaults to "us-east-1" for MinIO compatibility.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Whether to use path-style addressing (MinIO requires true; S3/DO Spaces require false).
    /// Path-style: http://endpoint/bucket/key
    /// Virtual-hosted-style: http://bucket.endpoint/key
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Bucket post-job artifacts (reports/charts/CSVs/bundles) are written to.</summary>
    public string ReportsBucket { get; set; } = "placecontext-reports";

    /// <summary>Bucket for baked job-dependency layers (the Kubernetes runner's warm cache).</summary>
    public string DepsBucket { get; set; } = "placecontext-deps";
}
