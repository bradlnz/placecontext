using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class SharedArtifactsControllerTests
{
    [Fact]
    public void Public_controller_explicitly_allows_anonymous_access() =>
        Assert.NotEmpty(
            typeof(SharedArtifactsController).GetCustomAttributes(
                typeof(AllowAnonymousAttribute),
                inherit: true
            )
        );

    [Fact]
    public async Task Valid_code_streams_artifact_with_no_store_and_no_referrer_headers()
    {
        var artifact = new SharedArtifact(
            "Report",
            "reports",
            "runs/customer-report.pdf",
            "application/pdf"
        );
        var controller = MakeController(artifact, out var http);

        var result = await controller.Get("pc_art_valid");

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("private, no-store", http.Response.Headers.CacheControl);
        Assert.Equal("no-referrer", http.Response.Headers["Referrer-Policy"]);
        Assert.Equal("noindex, nofollow, noarchive", http.Response.Headers["X-Robots-Tag"]);
    }

    [Fact]
    public async Task Invalid_code_is_not_found_without_touching_object_store()
    {
        var store = new StubStore(null);
        var controller = new SharedArtifactsController(new StubShares(null), store)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<NotFoundResult>(await controller.Get("pc_art_invalid"));
        Assert.Equal(0, store.OpenCount);
    }

    private static SharedArtifactsController MakeController(
        SharedArtifact artifact,
        out DefaultHttpContext http
    )
    {
        http = new DefaultHttpContext();
        return new SharedArtifactsController(new StubShares(artifact), new StubStore(artifact))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private sealed class StubShares(SharedArtifact? artifact) : IArtifactShareTokenService
    {
        public Task<ArtifactShareCreated> CreateOrRotateAsync(
            Guid artifactId,
            Guid createdByUserId,
            int lifetimeDays,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<ArtifactShareStatus?> GetStatusAsync(
            Guid artifactId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<bool> RevokeAsync(Guid artifactId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SharedArtifact?> ResolveAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(artifact);
    }

    private sealed class StubStore(SharedArtifact? artifact) : IObjectStore
    {
        public int OpenCount { get; private set; }
        public bool IsEnabled => true;
        public string ReportsBucket => "reports";
        public string DepsBucket => "deps";

        public Task<ObjectDownload?> OpenReadAsync(
            string bucket,
            string key,
            CancellationToken ct = default
        )
        {
            OpenCount++;
            return Task.FromResult<ObjectDownload?>(
                artifact is null
                    ? null
                    : new ObjectDownload(
                        new MemoryStream(Encoding.UTF8.GetBytes("pdf")),
                        artifact.ContentType
                    )
            );
        }

        public Task PutAsync(
            string bucket,
            string key,
            byte[] content,
            string contentType,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task EnsureBucketAsync(string bucket, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<string> PresignDownloadAsync(
            string bucket,
            string key,
            TimeSpan ttl,
            CancellationToken ct = default
        ) => Task.FromResult("");

        public Task<string> PresignUploadAsync(
            string bucket,
            string key,
            TimeSpan ttl,
            CancellationToken ct = default
        ) => Task.FromResult("");
    }
}
