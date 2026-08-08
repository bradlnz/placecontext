using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Authenticated, constrained OpenSearch proxy. Credentials stay server-side; callers can search,
/// discover fields, and request bounded chart aggregations, but cannot send arbitrary cluster DSL.
/// </summary>
[ApiController]
[Authorize(Policy = Permission.DataRead)]
[Route("api/v1/projects/{projectId:guid}/opensearch")]
public sealed class OpenSearchProxyController : ControllerBase
{
    private readonly IPlaceContextService _service;
    public OpenSearchProxyController(IPlaceContextService service) => _service = service;

    [HttpGet("indices")]
    public Task<IReadOnlyList<OpenSearchIndexView>> Indices(
        Guid projectId, CancellationToken ct)
        => _service.ListOpenSearchIndicesAsync(projectId, ct);

    [HttpGet("fields")]
    public Task<IReadOnlyList<OpenSearchFieldView>> Fields(
        Guid projectId, [FromQuery] string index, CancellationToken ct)
        => _service.ListOpenSearchFieldsAsync(projectId, index, ct);

    [HttpPost("search")]
    public Task<OpenSearchSearchView> Search(
        Guid projectId, [FromBody] OpenSearchProxySearchRequest request, CancellationToken ct)
        => _service.SearchOpenSearchAsync(new OpenSearchSearchRequest(
            projectId, request.IndexPattern, request.QueryText, request.Page, request.PageSize,
            request.BucketField, request.BucketType, request.ChartType, request.MetricType,
            request.MetricField, request.DateInterval), ct);
}
