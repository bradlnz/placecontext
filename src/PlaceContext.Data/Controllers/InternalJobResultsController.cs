using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Features;
using PlaceContext.Data.Contracts.Api;

namespace PlaceContext.Data.Controllers;

[ApiController]
[Route("api/data/internal/job-results")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalJobResultsController(
    DataMappingIngestionService mappings,
    EntityTagService entityTags) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Process(
        [FromBody] ProcessJobResultRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceKind == "chain")
        {
            await mappings.IngestChainOutputAsync(
                request.SourceId,
                request.RunId,
                request.ProjectId,
                request.PrimaryOutput,
                cancellationToken);
            return Accepted();
        }

        if (request.SourceKind != "job")
            return BadRequest(new { error = "sourceKind must be 'job' or 'chain'" });

        await mappings.IngestJobOutputAsync(
            request.SourceId,
            request.RunId,
            request.ProjectId,
            request.PrimaryOutput,
            cancellationToken);
        await entityTags.TagRunOutputAsync(
            request.SourceId,
            request.RunId,
            request.ProjectId,
            request.PrimaryOutput,
            request.Documents
                .Select(document => new RunDocumentContent(
                    document.Name,
                    document.Content,
                    document.IsBinary))
                .ToList(),
            cancellationToken);
        return Accepted();
    }
}
