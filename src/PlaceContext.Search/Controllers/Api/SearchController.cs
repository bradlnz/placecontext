using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Search.Contracts.Api;

namespace PlaceContext.Search.Controllers.Api;

/// <summary>
/// Project-scoped workspace search API. The project is resolved from X-Project-Id or X-Project by
/// the hosting request pipeline.
/// </summary>
[ApiController]
[Route("api/v1/search")]
[Authorize(Policy = Permission.DataRead)]
[Produces("application/json")]
public sealed class SearchController(IDispatcher dispatcher, ICurrentProject project) : ControllerBase
{
    private const int SearchCandidateLimit = 100;

    /// <summary>
    /// GET /api/v1/search?q=...&amp;limit=25 — searches the project resolved from X-Project-Id or
    /// X-Project.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SearchApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int limit = 25)
    {
        if (!project.IsResolved)
        {
            return BadRequest(new
            {
                error = "No project resolved. Pass X-Project-Id (GUID) or X-Project (name) on the request.",
            });
        }

        var term = q?.Trim() ?? "";
        if (term.Length < 2)
            return BadRequest(new { error = "q must contain at least 2 characters." });
        if (term.Length > 200)
            return BadRequest(new { error = "q must be 200 characters or fewer." });
        if (limit is < 1 or > 100)
            return BadRequest(new { error = "limit must be between 1 and 100." });

        var projectId = project.ProjectId;
        var results = await dispatcher.Query(
            new SearchQuery(term, SearchCandidateLimit, projectId),
            HttpContext.RequestAborted);
        return Ok(SearchApiMapper.ToResponse(results, projectId, limit));
    }
}
