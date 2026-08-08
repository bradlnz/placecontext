using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize(Policy = Permission.JobsView)]
[Produces("application/json")]
public sealed class JobsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}")]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListJobsQuery(projectId), ct));

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
        => await dispatcher.Query(new GetJobQuery(jobId), ct) is { } job ? Ok(job) : NotFound();

    [HttpGet("projects/{projectId:guid}/chains")]
    public async Task<IActionResult> ListChains(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListJobChainsQuery(projectId), ct));

    [HttpGet("projects/{projectId:guid}/schedules")]
    [Authorize(Policy = Permission.TriggersManage)]
    public async Task<IActionResult> ListSchedules(Guid projectId, CancellationToken ct)
        => Ok(await dispatcher.Query(new ListTriggersQuery(projectId), ct));
}
