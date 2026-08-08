using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Controllers;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Host.Tests;

/// <summary>
/// Pins the response contract the Artifacts page's iframe preview depends on. Two regressions have
/// already shipped here: HTML forced to download (blank preview), then a client-side sandbox
/// attribute (opaque origin → SameSite=Lax auth cookie withheld → /locked in the frame). The rule:
/// sandboxing of untrusted HTML/SVG lives in THIS response's CSP header — never in a sandbox
/// attribute on the portal's iframe — so the fetch stays authenticated while the rendered document
/// still gets scripts stripped and an opaque origin.
/// </summary>
public class ArtifactsControllerTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    public async Task Html_and_svg_render_inline_under_a_csp_sandbox(string contentType)
    {
        var (result, headers) = await GetAsync(contentType);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.StartsWith("text/html", file.ContentType);
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName)); // inline, not a download
        Assert.Equal(
            "sandbox allow-popups; frame-ancestors 'self'",
            headers["Content-Security-Policy"]
        );
        Assert.Equal("SAMEORIGIN", headers["X-Frame-Options"]);
        Assert.StartsWith("inline;", headers.ContentDisposition.ToString());
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("video/mp4")]
    public async Task Other_previewable_types_render_inline_without_the_sandbox(string contentType)
    {
        var (result, headers) = await GetAsync(contentType);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(contentType, file.ContentType);
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName));
        // No "sandbox" — it blanks the built-in PDF viewer and buys nothing for non-active content.
        Assert.Equal("frame-ancestors 'self'", headers["Content-Security-Policy"]);
        Assert.Equal("SAMEORIGIN", headers["X-Frame-Options"]);
        Assert.StartsWith("inline;", headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task Non_previewable_types_download_as_attachments()
    {
        var (result, _) = await GetAsync("application/zip", objectKey: "runs/x/bundle.zip");

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("bundle.zip", file.FileDownloadName);
    }

    [Fact]
    public async Task Generic_object_metadata_falls_back_to_the_durable_artifact_image_type()
    {
        var link = MakeLink("image/png", "runs/x/preview.png");
        var controller = MakeController(link, out _, "application/octet-stream");

        var result = await controller.Get(RunId, link.Id);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName));
    }

    [Fact]
    public async Task Mismatched_run_id_is_not_found()
    {
        var link = MakeLink("text/html", "runs/x/report.html");
        var controller = MakeController(link, out _);

        var result = await controller.Get(Guid.NewGuid(), link.Id); // wrong run for this artifact

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Artifact not found", notFound.Value);
    }

    private static async Task<(IActionResult Result, IHeaderDictionary Headers)> GetAsync(
        string contentType,
        string objectKey = "runs/x/artifact.bin"
    )
    {
        var link = MakeLink(contentType, objectKey);
        var controller = MakeController(link, out var http);
        var result = await controller.Get(RunId, link.Id);
        return (result, http.Response.Headers);
    }

    private static RunArtifactLink MakeLink(string contentType, string objectKey) =>
        RunArtifactLink.Create(
            RunId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            PostJobActionKind.HtmlReport,
            "artifact",
            "reports",
            objectKey,
            contentType,
            3,
            DateTimeOffset.UtcNow
        );

    private static ArtifactDownloadsController MakeController(
        RunArtifactLink link,
        out DefaultHttpContext http,
        string? storedContentType = null
    )
    {
        http = new DefaultHttpContext();
        return new ArtifactDownloadsController(new StubLinks(link), new StubStore(link, storedContentType))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private sealed class StubLinks(RunArtifactLink link) : IRunArtifactLinkRepository
    {
        public Task<RunArtifactLink?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == link.Id ? link : null);

        public Task AddAsync(RunArtifactLink l, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RunArtifactLink>> ListForRunAsync(
            Guid runId,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<RunArtifactLink>> ListForJobAsync(
            Guid jobId,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<RunArtifactLink>> ListRecentAsync(
            int take,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<RunArtifactLink>> ListForProjectAsync(
            Guid projectId,
            int take,
            string? search = null,
            CancellationToken ct = default
        ) => Empty();

        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<RunArtifactLink>> ListPendingOcrAsync(
            int take,
            CancellationToken ct = default
        ) => Empty();

        public Task MarkOcrProcessedAsync(
            Guid artifactId,
            DateTimeOffset processedAt,
            string? error,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        private static Task<IReadOnlyList<RunArtifactLink>> Empty() =>
            Task.FromResult<IReadOnlyList<RunArtifactLink>>(Array.Empty<RunArtifactLink>());
    }

    private sealed class StubStore(RunArtifactLink link, string? storedContentType = null)
        : IObjectStore
    {
        public bool IsEnabled => true;
        public string ReportsBucket => link.Bucket;
        public string DepsBucket => "placecontext-deps";

        public Task<ObjectDownload?> OpenReadAsync(
            string bucket,
            string key,
            CancellationToken ct = default
        ) =>
            Task.FromResult<ObjectDownload?>(
                bucket == link.Bucket && key == link.ObjectKey
                    ? new ObjectDownload(
                        new MemoryStream(Encoding.UTF8.GetBytes("hi!")),
                        storedContentType ?? link.ContentType
                    )
                    : null
            );

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
