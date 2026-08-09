using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

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
