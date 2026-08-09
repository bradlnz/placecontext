using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Artifacts.Contracts.Api;
using PlaceContext.Artifacts.Controllers;

namespace PlaceContext.Artifacts.Tests;

public sealed class InternalCrmArtifactControllerTests
{
    [Fact]
    public async Task Read_crm_object_returns_the_storage_content_contract()
    {
        var content = new byte[] { 1, 2, 3, 4 };
        var controller = new InternalJobArtifactsController(
            new StubStore(new ObjectDownload(
                new MemoryStream(content, writable: false),
                "application/pdf")),
            null!,
            null!,
            new StubClock());

        var result = await controller.ReadCrmObject(
            new CrmObjectReferenceRequest("reports", "crm/report.pdf"),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(
            Convert.ToBase64String(content),
            ok.Value!.GetType().GetProperty("contentBase64")!.GetValue(ok.Value));
        Assert.Equal(
            "application/pdf",
            ok.Value.GetType().GetProperty("ContentType")!.GetValue(ok.Value));
    }

    [Fact]
    public async Task Read_crm_object_returns_not_found_when_storage_has_no_value()
    {
        var controller = new InternalJobArtifactsController(
            new StubStore(null),
            null!,
            null!,
            new StubClock());

        var result = await controller.ReadCrmObject(
            new CrmObjectReferenceRequest("reports", "missing"),
            default);

        Assert.IsType<NotFoundResult>(result);
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class StubStore(ObjectDownload? download) : IObjectStore
    {
        public bool IsEnabled => true;
        public string ReportsBucket => "reports";
        public string DepsBucket => "deps";

        public Task PutAsync(
            string bucket,
            string key,
            byte[] content,
            string contentType,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ObjectDownload?> OpenReadAsync(
            string bucket,
            string key,
            CancellationToken ct = default)
            => Task.FromResult(download);

        public Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task EnsureBucketAsync(string bucket, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> PresignDownloadAsync(
            string bucket,
            string key,
            TimeSpan ttl,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> PresignUploadAsync(
            string bucket,
            string key,
            TimeSpan ttl,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
