namespace PlaceContext.Application.Ports;

/// <summary>
/// Port: an S3-compatible object store (MinIO in-cluster) for run artifacts — generated HTML reports,
/// charts, CSVs, and raw output bundles produced by post-job actions. PlaceContext treats object
/// content as opaque bytes; the key namespace is the caller's concern.
/// </summary>
public interface IObjectStore
{
    /// <summary>Whether a real object store is configured. When false, callers skip artifact storage.</summary>
    bool IsEnabled { get; }

    /// <summary>The default bucket post-job artifacts are written to.</summary>
    string ReportsBucket { get; }

    /// <summary>The bucket baked job-dependency layers (the warm cache) are written to.</summary>
    string DepsBucket { get; }

    /// <summary>Uploads bytes under (bucket, key) with the given content type. Idempotent (overwrites).</summary>
    Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct = default);

    /// <summary>Opens an object for reading, or null if it doesn't exist. Caller disposes the result.</summary>
    Task<ObjectDownload?> OpenReadAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>Deletes an object. Idempotent — a missing object is not an error. No-op when disabled.</summary>
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>Whether an object exists. False when the store is disabled.</summary>
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>Creates the bucket when absent (idempotent). No-op when the store is disabled.</summary>
    Task EnsureBucketAsync(string bucket, CancellationToken ct = default);

    /// <summary>
    /// A time-limited presigned GET URL for an object, for direct client access (e.g. a job pod
    /// fetching a dependency-cache tarball). The object is served RAW — presigned traffic bypasses
    /// the store's encryption-at-rest, so only use it for content that is not encrypted (never for
    /// post-job artifacts).
    /// </summary>
    Task<string> PresignDownloadAsync(string bucket, string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// A time-limited presigned PUT URL, for direct client upload (e.g. a bake Job publishing a
    /// dependency-cache tarball). The object lands RAW — see <see cref="PresignDownloadAsync"/>.
    /// </summary>
    Task<string> PresignUploadAsync(string bucket, string key, TimeSpan ttl, CancellationToken ct = default);
}

/// <summary>A readable object: its content stream plus the stored content type. Dispose to release.</summary>
public sealed record ObjectDownload(Stream Content, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
