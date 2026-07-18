using Amazon;
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
            // The SDK picks SigV2 for presigned URLs by default (with a US-East-1 fallback) — the
            // dependency-cache pods need SigV4, and the only S3 usage in this process is this store.
            AWSConfigsS3.UseSignatureVersion4 = true;
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

    public string DepsBucket => _o.DepsBucket;

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

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        if (_client is null) return false;
        try
        {
            await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = key }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Creates the bucket if absent (idempotent). No-op when the store is disabled.</summary>
    public async Task EnsureBucketAsync(string bucket, CancellationToken ct)
    {
        if (_client is null) return;
        if (await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_client, bucket)) return;
        try { await _client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct); }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists") { }
    }

    // Presigned URLs give clients (job pods) direct raw-object access — used for the dependency
    // cache, whose tarballs are not encrypted. SigV4 signing is computed locally (no round trip).
    public Task<string> PresignDownloadAsync(string bucket, string key, TimeSpan ttl, CancellationToken ct = default)
        => PresignAsync(bucket, key, HttpVerb.GET, ttl);

    public Task<string> PresignUploadAsync(string bucket, string key, TimeSpan ttl, CancellationToken ct = default)
        => PresignAsync(bucket, key, HttpVerb.PUT, ttl);

    private Task<string> PresignAsync(string bucket, string key, HttpVerb verb, TimeSpan ttl)
    {
        if (_client is null) throw new InvalidOperationException("Object store is not configured.");
        return _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = verb,
            Expires = DateTime.UtcNow.Add(ttl),
            // The SDK otherwise presigns HTTPS regardless of the endpoint — in-cluster MinIO
            // serves plain HTTP, so follow the configured endpoint's scheme.
            Protocol = _o.Endpoint.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? Protocol.HTTPS : Protocol.HTTP,
        });
    }

    public void Dispose() => _client?.Dispose();
}
