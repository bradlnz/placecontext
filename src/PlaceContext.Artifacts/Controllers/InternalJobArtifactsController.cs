using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Artifacts.Contracts.Api;

namespace PlaceContext.Artifacts.Controllers;

[ApiController]
[Route("api/artifacts/internal/job-output")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalJobArtifactsController(
    IObjectStore store,
    IRunArtifactLinkRepository links,
    IArtifactsUnitOfWork unitOfWork,
    IClock clock,
    IContentIndexer? contentIndexer = null) : ControllerBase
{
    [HttpGet("/api/artifacts/internal/runs/{runId:guid}")]
    public async Task<IActionResult> ListRun(Guid runId, CancellationToken ct)
    {
        var artifacts = await links.ListForRunAsync(runId, ct);
        return Ok(artifacts.Select(artifact => new
        {
            artifact.Id,
            artifact.RunId,
            artifact.JobId,
            artifact.Title,
            artifact.Bucket,
            artifact.ObjectKey,
            artifact.ContentType,
            artifact.SizeBytes,
            artifact.CreatedAt,
        }));
    }

    [HttpGet("/api/artifacts/internal/projects/{projectId:guid}")]
    public async Task<IActionResult> ListProject(
        Guid projectId,
        [FromQuery] int take = 25,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var artifacts = await links.ListForProjectAsync(
            projectId,
            Math.Clamp(take, 1, 200),
            search,
            ct);
        return Ok(artifacts.Select(artifact => new
        {
            artifact.Id,
            artifact.RunId,
            artifact.JobId,
            artifact.Title,
            kind = artifact.Kind.ToString(),
            artifact.ContentType,
            artifact.SizeBytes,
            artifact.CreatedAt,
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Store(StoreJobArtifactRequest request, CancellationToken ct)
    {
        if (!store.IsEnabled) return Problem("The Artifacts object store is disabled.", statusCode: 503);
        if (!Enum.TryParse<PostJobActionKind>(request.Kind, true, out var kind))
            return BadRequest(new { error = "Unknown artifact kind." });

        var content = Convert.FromBase64String(request.ContentBase64);
        var bucket = store.ReportsBucket;
        var safeName = Path.GetFileName(request.FileName);
        var key = $"runs/{request.RunId:N}/{safeName}";
        await store.PutAsync(bucket, key, content, request.ContentType, ct);
        await links.AddAsync(RunArtifactLink.Create(
            request.RunId, request.JobId, request.ProjectId, kind, request.Title,
            bucket, key, request.ContentType, content.LongLength, clock.UtcNow), ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (contentIndexer is { IsEnabled: true } && content.Length > 0)
        {
            try
            {
                var bytes = content.Length > 8000 ? content[..8000] : content;
                var text = Encoding.UTF8.GetString(bytes);
                if (!string.IsNullOrWhiteSpace(text))
                    await contentIndexer.IndexAsync(
                        request.ProjectId, ContentKind.Document, $"run/{request.RunId:N}/{safeName}",
                        $"{request.JobName} — {request.Title}\n\n{text}", ct);
            }
            catch { }
        }
        return Accepted();
    }

    [HttpPost("/api/artifacts/internal/crm-objects")]
    public async Task<IActionResult> StoreCrmObject(StoreCrmObjectRequest request, CancellationToken ct)
    {
        if (!store.IsEnabled) return Problem("The Artifacts object store is disabled.", statusCode: 503);
        var content = Convert.FromBase64String(request.ContentBase64);
        var bucket = store.ReportsBucket;
        var key = $"crm-clients/{request.ProjectId:N}/{request.ClientId:N}/{request.ObjectId:N}/content";
        await store.PutAsync(bucket, key, content, request.ContentType, ct);
        return Ok(new { bucket, objectKey = key });
    }

    [HttpPost("/api/artifacts/internal/crm-objects/read")]
    public async Task<IActionResult> ReadCrmObject(
        CrmObjectReferenceRequest request,
        CancellationToken ct)
    {
        var value = await store.OpenReadAsync(request.Bucket, request.ObjectKey, ct);
        if (value is null) return NotFound();
        await using var buffer = new MemoryStream();
        await value.Content.CopyToAsync(buffer, ct);
        return Ok(new
        {
            contentBase64 = Convert.ToBase64String(buffer.ToArray()),
            value.ContentType,
        });
    }

    [HttpDelete("/api/artifacts/internal/crm-objects")]
    public async Task<IActionResult> DeleteCrmObject(
        CrmObjectReferenceRequest request,
        CancellationToken ct)
    {
        await store.DeleteAsync(request.Bucket, request.ObjectKey, ct);
        return NoContent();
    }

    public sealed record StoreJobArtifactRequest(
        Guid ProjectId,
        Guid JobId,
        Guid RunId,
        string JobName,
        string Kind,
        string FileName,
        string Title,
        string ContentType,
        string ContentBase64);
}
