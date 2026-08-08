using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Artifacts.Infrastructure.Storage;

namespace PlaceContext.Artifacts.Tests;

/// <summary>
/// <see cref="S3ObjectStore"/> presigned URLs (the dependency-cache channel job pods use) —
/// SigV4 signing is computed locally, so these need no live store.
/// </summary>
public class S3ObjectStoreTests
{
    private static S3ObjectStore Create(string endpoint = "http://minio:9000") => new(
        Options.Create(new ObjectStoreOptions { Endpoint = endpoint, AccessKey = "ak", SecretKey = "sk" }),
        new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));

    [Fact]
    public async Task Presigned_download_targets_endpoint_bucket_and_key_with_the_requested_expiry()
    {
        var url = await Create().PresignDownloadAsync("placecontext-deps", "python/abc123.tar.gz", TimeSpan.FromHours(1));

        Assert.StartsWith("http://minio:9000/placecontext-deps/python/abc123.tar.gz?", url); // path-style
        AssertExpiryWithin(url, TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Presigned_upload_carries_its_own_expiry()
    {
        var url = await Create().PresignUploadAsync("placecontext-deps", "k", TimeSpan.FromMinutes(15));

        Assert.Contains("/placecontext-deps/k?", url);
        AssertExpiryWithin(url, TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task A_disabled_store_cannot_presign()
    {
        var store = new S3ObjectStore(Options.Create(new ObjectStoreOptions()),
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));
        Assert.False(store.IsEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PresignDownloadAsync("b", "k", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task S3_presigned_download_uses_virtual_hosted_style()
    {
        var store = new S3ObjectStore(
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
        var store = new S3ObjectStore(
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
        var store = new S3ObjectStore(
            Options.Create(new ObjectStoreOptions
            {
                Endpoint = "http://minio:9000",
                AccessKey = "ak",
                SecretKey = "sk",
                Region = "us-east-1",
                ForcePathStyle = true
            }),
            new DataProtectionEncryptor(new EphemeralDataProtectionProvider()));

        var url = await store.PresignDownloadAsync("my-bucket", "key/file.txt", TimeSpan.FromHours(1));

        Assert.StartsWith("http://minio:9000/my-bucket/key/file.txt?", url);
        AssertExpiryWithin(url, TimeSpan.FromHours(1));
    }

    private static void AssertExpiryWithin(string url, TimeSpan requestedTtl)
    {
        var expiryParameter = new Uri(url)
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Single(parameter => parameter.StartsWith("X-Amz-Expires=", StringComparison.Ordinal));
        var actualSeconds = int.Parse(expiryParameter[(expiryParameter.IndexOf('=') + 1)..]);
        var requestedSeconds = (int)requestedTtl.TotalSeconds;

        Assert.InRange(actualSeconds, requestedSeconds - 2, requestedSeconds);
    }
}
