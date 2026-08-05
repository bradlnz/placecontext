using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers;
using Xunit;

namespace PlaceContext.Host.Tests;

/// <summary>
/// Pins the ChatAttachmentsController contract: the key's {tenantId} segment is the cross-tenant
/// guard (a wrong/foreign tenant id looks exactly like a missing object), and the response reuses
/// the artifact preview contract — previewable types inline under the CSP sandbox, everything else
/// downloads with the original file name recovered from the key's last segment.
/// </summary>
public class ChatAttachmentsControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    private static string Key(string fileName = "report.pdf", Guid? tenantId = null) =>
        $"chat/{tenantId ?? TenantId}/{ProjectId}/{SessionId}/{Guid.NewGuid()}-{fileName}";

    [Fact]
    public async Task Happy_path_streams_the_stored_bytes_inline()
    {
        var key = Key("notes.txt");
        var controller = MakeController(servedKey: key, "text/plain", out var http);

        var result = await controller.Get(key);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("text/plain", file.ContentType);
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName)); // inline, not a download
        using var reader = new StreamReader(file.FileStream);
        Assert.Equal("hi!", await reader.ReadToEndAsync());
        Assert.Equal("frame-ancestors 'self'", http.Response.Headers["Content-Security-Policy"]);
        Assert.Equal("SAMEORIGIN", http.Response.Headers["X-Frame-Options"]);
        Assert.StartsWith("inline;", http.Response.Headers.ContentDisposition.ToString());
        Assert.Contains("notes.txt", http.Response.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task Html_renders_inline_under_a_csp_sandbox()
    {
        var key = Key("page.html");
        var controller = MakeController(servedKey: key, "text/html", out var http);

        var result = await controller.Get(key);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.StartsWith("text/html", file.ContentType);
        Assert.True(string.IsNullOrEmpty(file.FileDownloadName));
        Assert.Equal(
            "sandbox allow-popups; frame-ancestors 'self'",
            http.Response.Headers["Content-Security-Policy"]
        );
    }

    [Fact]
    public async Task Cross_tenant_key_is_not_found()
    {
        var key = Key(tenantId: Guid.NewGuid()); // a different tenant's attachment
        var controller = MakeController(servedKey: key, "text/plain", out _);

        var result = await controller.Get(key);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("chat")]
    [InlineData("chat/3fa85f64-5717-4562-b3fc-2c963f66afa6/proj/sess")] // too few segments
    [InlineData("chat/not-a-guid/proj/sess/3fa85f64-file.txt")] // tenant segment not a guid
    [InlineData("other/3fa85f64-5717-4562-b3fc-2c963f66afa6/proj/sess/3fa85f64-file.txt")] // wrong prefix
    public async Task Malformed_keys_are_not_found(string key)
    {
        var controller = MakeController(servedKey: null, "text/plain", out _);

        var result = await controller.Get(key);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Disabled_store_is_not_found()
    {
        var key = Key();
        var controller = MakeController(servedKey: key, "text/plain", out _, storeEnabled: false);

        var result = await controller.Get(key);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Missing_object_is_not_found()
    {
        var key = Key();
        var controller = MakeController(servedKey: null, "text/plain", out _); // store holds nothing

        var result = await controller.Get(key);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Non_previewable_types_download_with_the_embedded_file_name()
    {
        var key = Key("bundle.zip");
        var controller = MakeController(servedKey: key, "application/zip", out _);

        var result = await controller.Get(key);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal("bundle.zip", file.FileDownloadName);
    }

    [Fact]
    public async Task Missing_embedded_file_name_falls_back_to_attachment()
    {
        var key = $"chat/{TenantId}/{ProjectId}/{SessionId}/{Guid.NewGuid()}"; // no '-name' suffix
        var controller = MakeController(servedKey: key, "application/zip", out _);

        var result = await controller.Get(key);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("attachment", file.FileDownloadName);
    }

    private static ChatAttachmentsController MakeController(
        string? servedKey,
        string contentType,
        out DefaultHttpContext http,
        bool storeEnabled = true
    )
    {
        http = new DefaultHttpContext();
        return new ChatAttachmentsController(
            new StubTenant(TenantId),
            new StubStore(storeEnabled, servedKey, contentType)
        )
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private sealed class StubTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid TenantId => tenantId;
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => true;
    }

    private sealed class StubStore(bool enabled, string? servedKey, string contentType)
        : IObjectStore
    {
        public bool IsEnabled => enabled;
        public string ReportsBucket => "placecontext-reports";
        public string DepsBucket => "placecontext-deps";

        public Task<ObjectDownload?> OpenReadAsync(
            string bucket,
            string key,
            CancellationToken ct = default
        ) =>
            Task.FromResult<ObjectDownload?>(
                enabled && bucket == ChatAttachmentsController.Bucket && key == servedKey
                    ? new ObjectDownload(
                        new MemoryStream(Encoding.UTF8.GetBytes("hi!")),
                        contentType
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
