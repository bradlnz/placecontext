using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

/// <summary>
/// Project-scoped workspace search API. Project resolution, authentication, and permissions match
/// the entity data API so the same personal token and X-Project headers work for both surfaces.
/// </summary>
[ApiController]
[Route("api/v1/search")]
[Authorize(AuthenticationSchemes =
    UserApiTokenAuthenticationHandler.SchemeName + "," + ApiKeyAuthenticationHandler.SchemeName)]
[Produces("application/json")]
public sealed class SearchController : ControllerBase
{
    private const int SearchCandidateLimit = 100;
    private readonly PlaceContextService _svc;
    private readonly ICurrentProject _project;

    public SearchController(PlaceContextService svc, ICurrentProject project)
        => (_svc, _project) = (svc, project);

    /// <summary>
    /// GET /api/v1/search?q=...&amp;limit=25 — searches the project resolved from X-Project-Id or
    /// X-Project. Results can include project activity, decisions, artifacts, entity tags, indexed
    /// content, and configured OpenSearch documents.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permission.DataRead)]
    [ProducesResponseType(typeof(SearchApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int limit = 25)
    {
        if (!_project.IsResolved)
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

        var projectId = _project.ProjectId!.Value;
        var results = await _svc.SearchAsync(
            term, projectId, SearchCandidateLimit, HttpContext.RequestAborted);
        return Ok(SearchApiMapper.ToResponse(results, projectId, limit));
    }
}

public sealed record SearchApiResponse(
    string Query,
    Guid ProjectId,
    int Count,
    IReadOnlyList<SearchApiHitResponse> Hits);

public sealed record SearchApiHitResponse(
    string Kind,
    Guid ProjectId,
    string Title,
    string Subtitle,
    string Url);

public static class SearchApiMapper
{
    public static SearchApiResponse ToResponse(SearchResultsView results, Guid projectId, int limit)
    {
        var hits = results.Hits
            .Where(hit => hit.ProjectId == projectId)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(hit => new SearchApiHitResponse(
                hit.Kind, hit.ProjectId, hit.Title, hit.Subtitle, hit.Url))
            .ToList();
        return new SearchApiResponse(results.Term, projectId, hits.Count, hits);
    }
}
