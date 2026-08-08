using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Controllers;

/// <summary>
/// Authenticated, constrained OpenSearch access. Credentials remain in the Search service;
/// callers can only use the bounded operations represented by Search-owned queries.
/// </summary>
[ApiController]
[Authorize(Policy = Permission.DataRead)]
[Route("api/v1/projects/{projectId:guid}/opensearch")]
public sealed class OpenSearchController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("indices")]
    public async Task<IActionResult> Indices(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListOpenSearchIndicesQuery(projectId), ct));

    [HttpGet("fields")]
    public async Task<IActionResult> Fields(
        Guid projectId,
        [FromQuery] string index,
        CancellationToken ct)
        => Ok(await dispatcher.Query(new ListOpenSearchFieldsQuery(projectId, index), ct));

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        Guid projectId,
        [FromBody] OpenSearchProxySearchRequest request,
        CancellationToken ct)
        => Ok(await dispatcher.Query(new SearchOpenSearchQuery(new OpenSearchSearchRequest(
            projectId,
            request.IndexPattern,
            request.QueryText,
            request.Page,
            request.PageSize,
            request.BucketField,
            request.BucketType,
            request.ChartType,
            request.MetricType,
            request.MetricField,
            request.DateInterval)), ct));
}
