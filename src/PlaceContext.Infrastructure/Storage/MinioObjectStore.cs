using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStore"/> backed by an S3-compatible store (MinIO in-cluster) via the AWS SDK with
/// path-style addressing. Used to persist post-job artifacts (HTML reports, charts, CSVs, raw bundles).
/// Objects are encrypted at rest with <see cref="IDataEncryptor"/> so MinIO disk alone cannot read them.
/// Disabled (no-op, IsEnabled=false) when no endpoint/credentials are configured, so the run still
/// completes when object storage isn't deployed.
/// </summary>
public sealed class MinioObjectStore : IObjectStore, IDisposable
{
    private readonly ObjectStoreOptions _o;
    private readonly IDataEncryptor _enc;
    private readonly AmazonS3Client? _client;
    private static string P => IDataEncryptor.Purpose.ObjectStore;

    public MinioObjectStore(IOptions<ObjectStoreOptions> options, IDataEncryptor enc)
    {
        _o = options.Value;
        _enc = enc;
        if (IsEnabled)
        {
            var cfg = new AmazonS3Config
            {
                ServiceURL = _o.Endpoint,
                ForcePathStyle = true,            // MinIO uses path-style (no virtual-host buckets)
                AuthenticationRegion = "us-east-1",
            };
            _client = new AmazonS3Client(_o.AccessKey, _o.SecretKey, cfg);
        }
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_o.Endpoint)
        && !string.IsNullOrWhiteSpace(_o.AccessKey) && !string.IsNullOrWhiteSpace(_o.SecretKey);

    public string ReportsBucket => _o.ReportsBucket;

    /// <summary>Hard cap on object size before encryption (matches field-encryptor max bytes).</summary>
    public const int MaxObjectBytes = Security.DataProtectionEncryptor.MaxPlaintextBytes;

    public async Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("Object store is not configured.");
        if (content.Length > MaxObjectBytes)
            throw new InvalidOperationException(
                $"Object too large ({content.Length:N0} bytes; max {MaxObjectBytes:N0}).");
        await EnsureBucketAsync(bucket, ct);
        var cipher = _enc.ProtectBytes(content, P);
        using var ms = new MemoryStream(cipher, writable: false);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = ms,
            ContentType = contentType,
            AutoCloseStream = false,
            // NB: do NOT set DisablePayloadSigning — the SDK rejects it over plain HTTP
            // ("must be sent over HTTPS"). Normal SigV4 payload signing works fine with MinIO on HTTP.
        }, ct);
    }

    // Create the bucket if absent so artifact storage works even before the make-bucket Job has run.
    private async Task EnsureBucketAsync(string bucket, CancellationToken ct)
    {
        if (await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_client!, bucket)) return;
        try { await _client!.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct); }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists") { }
    }

    public async Task<ObjectDownload?> OpenReadAsync(string bucket, string key, CancellationToken ct = default)
    {
        if (_client is null) return null;
        try
        {
            var resp = await _client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, ct);
            var ctype = string.IsNullOrWhiteSpace(resp.Headers.ContentType) ? "application/octet-stream" : resp.Headers.ContentType;
            // Buffer + decrypt so consumers see plaintext (legacy unencrypted objects pass through).
            await using var raw = resp.ResponseStream;
            using var buf = new MemoryStream();
            await raw.CopyToAsync(buf, ct);
            var plain = _enc.UnprotectBytes(buf.ToArray(), P);
            return new ObjectDownload(new MemoryStream(plain, writable: false), ctype);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
    {
        if (_client is null) return;                 // store disabled — nothing was ever written
        try
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = key }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone — deletion is idempotent.
        }
    }

    public void Dispose() => _client?.Dispose();
}
