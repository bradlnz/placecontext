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

    /// <summary>Uploads bytes under (bucket, key) with the given content type. Idempotent (overwrites).</summary>
    Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct = default);

    /// <summary>Opens an object for reading, or null if it doesn't exist. Caller disposes the result.</summary>
    Task<ObjectDownload?> OpenReadAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>Deletes an object. Idempotent — a missing object is not an error. No-op when disabled.</summary>
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);
}

/// <summary>A readable object: its content stream plus the stored content type. Dispose to release.</summary>
public sealed record ObjectDownload(Stream Content, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
