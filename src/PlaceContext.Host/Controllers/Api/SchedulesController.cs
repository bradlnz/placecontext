using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Auth;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Management;

namespace PlaceContext.Host.Controllers.Api;

/// <summary>
/// Management API: schedules — cron and event triggers on a job. See docs/management-api.md.
/// Authenticated via the ApiKey scheme only (see <see cref="ApiKeyAuthenticationHandler"/>), gated by
/// the single triggers.manage permission (the catalog has no finer-grained triggers.view — reads and
/// writes both require it), and implicitly tenant-scoped by <c>TenantResolutionMiddleware</c> + EF's
/// global query filter.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName, Policy = Permission.TriggersManage)]
[Produces("application/json")]
public sealed class SchedulesController : ControllerBase
{
    private readonly IPlaceContextService _svc;
    public SchedulesController(IPlaceContextService svc) => _svc = svc;

    /// <summary>GET /api/v1/projects/{projectId}/schedules — every schedule/event trigger under the
    /// project, or 404 if the project itself doesn't exist.</summary>
    [HttpGet("projects/{projectId:guid}/schedules")]
    public async Task<ActionResult<IReadOnlyList<ScheduleResponse>>> ListForProject(Guid projectId)
    {
        var ct = HttpContext.RequestAborted;
        if (await _svc.GetProjectByIdAsync(projectId, ct) is null) return NotFound();

        var triggers = await _svc.ListTriggersAsync(projectId, ct);
        return Ok(triggers.Select(ScheduleApiMapper.ToResponse).ToList());
    }

    /// <summary>
    /// POST /api/v1/projects/{projectId}/schedules — creates a cron schedule (Kind "Schedule", with
    /// CronExpression) or an event subscription (Kind "Event", with EventName) on a job under the
    /// project. 404 if the project or the referenced job doesn't exist; 400 if the job belongs to a
    /// different project, the kind is unrecognized, or the cron expression is invalid.
    /// </summary>
    [HttpPost("projects/{projectId:guid}/schedules")]
    public async Task<ActionResult<ScheduleResponse>> Create(Guid projectId, [FromBody] CreateScheduleRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (await _svc.GetProjectByIdAsync(projectId, ct) is null) return NotFound();

        var job = await _svc.GetJobAsync(request.JobId, ct);
        if (job is null) return NotFound(new { error = $"Job {request.JobId} not found." });
        if (job.ProjectId != projectId)
            return BadRequest(new { error = $"Job {request.JobId} belongs to a different project." });

        try
        {
            var trigger = await _svc.CreateTriggerAsync(
                new CreateTriggerCommand(request.JobId, request.Name, request.Kind, request.CronExpression, request.EventName), ct);
            var response = ScheduleApiMapper.ToResponse(trigger);
            return CreatedAtRoute("GetScheduleById", new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

}
