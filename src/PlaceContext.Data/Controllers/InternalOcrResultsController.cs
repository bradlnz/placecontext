using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Data.Contracts.Api;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/data/internal/ocr-results")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalOcrResultsController(IProjectDataStore store) : ControllerBase
{
    private static readonly IReadOnlyList<ProjectColumnSpec> Columns =
    [
        new("ingested_at", DataColumnTypes.Timestamptz, true, false),
        new("artifact_id", DataColumnTypes.Uuid, true, false),
        new("run_id", DataColumnTypes.Uuid, true, false),
        new("job_id", DataColumnTypes.Uuid, true, false),
        new("title", DataColumnTypes.Text, false, false),
        new("content_type", DataColumnTypes.Text, false, false),
        new("markdown", DataColumnTypes.Text, true, false),
    ];

    [HttpPost]
    public async Task<IActionResult> Store(
        [FromBody] StoreOcrResultRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProjectId == Guid.Empty || request.ArtifactId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Markdown))
            return BadRequest(new { error = "projectId, artifactId, and markdown are required." });

        IReadOnlyList<string?> row =
        [
            request.IngestedAt.ToString("O"),
            request.ArtifactId.ToString(),
            request.RunId.ToString(),
            request.JobId.ToString(),
            request.Title,
            request.ContentType,
            request.Markdown,
        ];
        await store.AppendReadOnlyRowsAsync(
            request.ProjectId,
            "ocr_results",
            Columns,
            [row],
            cancellationToken);
        return Accepted();
    }
}
