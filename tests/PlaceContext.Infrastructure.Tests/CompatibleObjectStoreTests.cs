using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Storage;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// <see cref="CompatibleObjectStore"/> presigned URLs (the dependency-cache channel job pods use) —
/// SigV4 signing is computed locally, so these need no live store.
/// </summary>
public class CompatibleObjectStoreTests
{
    private static CompatibleObjectStore Create(string endpoint = "http://minio:9000") => new(
        Options.Create(new ObjectStoreOptions { Endpoint = endpoint, AccessKey = "ak", SecretKey = "sk" }),
        new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));

    [Fact]
    public async Task Presigned_download_targets_endpoint_bucket_and_key_with_the_requested_expiry()
    {
        var started = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromHours(1);
        var url = await Create().PresignDownloadAsync("placecontext-deps", "python/abc123.tar.gz", lifetime);
        var finished = DateTimeOffset.UtcNow;

        Assert.StartsWith("http://minio:9000/placecontext-deps/python/abc123.tar.gz?", url); // path-style
        AssertExpiry(url, lifetime, started, finished);
    }

    [Fact]
    public async Task Presigned_upload_carries_its_own_expiry()
    {
        var started = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromMinutes(15);
        var url = await Create().PresignUploadAsync("placecontext-deps", "k", lifetime);
        var finished = DateTimeOffset.UtcNow;

        Assert.Contains("/placecontext-deps/k?", url);
        AssertExpiry(url, lifetime, started, finished);
    }

    [Fact]
    public async Task A_disabled_store_cannot_presign()
    {
        var store = new CompatibleObjectStore(Options.Create(new ObjectStoreOptions()),
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));
        Assert.False(store.IsEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PresignDownloadAsync("b", "k", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task S3_presigned_download_uses_virtual_hosted_style()
    {
        var store = new CompatibleObjectStore(
            Options.Create(new ObjectStoreOptions
            {
                Endpoint = "https://s3.us-east-1.amazonaws.com",
                AccessKey = "ak",
                SecretKey = "sk",
                Region = "us-east-1",
                ForcePathStyle = false
            }),
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));

        var url = await store.PresignDownloadAsync("my-bucket", "key/file.txt", TimeSpan.FromHours(1));

        // Virtual-hosted style: bucket.s3.region.endpoint/key
        Assert.StartsWith("https://my-bucket.s3.us-east-1.amazonaws.com/key/file.txt?", url);
    }

    [Fact]
    public async Task DigitalOcean_spaces_presigned_download_uses_virtual_hosted_style()
    {
        var store = new CompatibleObjectStore(
            Options.Create(new ObjectStoreOptions
            {
                Endpoint = "https://nyc3.digitaloceanspaces.com",
                AccessKey = "ak",
                SecretKey = "sk",
                Region = "nyc3",
                ForcePathStyle = false
            }),
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));

        var url = await store.PresignDownloadAsync("my-space", "data/report.csv", TimeSpan.FromMinutes(30));

        // Virtual-hosted style: bucket.region.endpoint/key
        Assert.StartsWith("https://my-space.nyc3.digitaloceanspaces.com/data/report.csv?", url);
    }

    [Fact]
    public async Task MinIO_presigned_download_uses_path_style()
    {
        var store = new CompatibleObjectStore(
            Options.Create(new ObjectStoreOptions
            {
                Endpoint = "http://minio:9000",
                AccessKey = "ak",
                SecretKey = "sk",
                Region = "us-east-1",
                ForcePathStyle = true
            }),
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));

        var started = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromHours(1);
        var url = await store.PresignDownloadAsync("my-bucket", "key/file.txt", lifetime);
        var finished = DateTimeOffset.UtcNow;

        Assert.StartsWith("http://minio:9000/my-bucket/key/file.txt?", url);
        AssertExpiry(url, lifetime, started, finished);
    }

    private static void AssertExpiry(string url, TimeSpan lifetime, DateTimeOffset started, DateTimeOffset finished)
    {
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        var signedAt = DateTimeOffset.ParseExact(query["X-Amz-Date"].ToString(), "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var seconds = int.Parse(query["X-Amz-Expires"].ToString(), CultureInfo.InvariantCulture);
        var expiresAt = signedAt.AddSeconds(seconds);
        // Signing consumes time; both signature fields have whole-second precision.
        Assert.InRange(expiresAt, started.Add(lifetime).AddSeconds(-2), finished.Add(lifetime));
    }

}
