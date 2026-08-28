using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;

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
    private readonly PlaceContextService _svc;
    public SchedulesController(PlaceContextService svc) => _svc = svc;

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

    /// <summary>GET /api/v1/schedules/{id} — a single schedule/event trigger, or 404.</summary>
    [HttpGet("schedules/{id:guid}", Name = "GetScheduleById")]
    public async Task<ActionResult<ScheduleResponse>> GetById(Guid id)
    {
        var trigger = await _svc.GetTriggerAsync(id, HttpContext.RequestAborted);
        return trigger is null ? NotFound() : Ok(ScheduleApiMapper.ToResponse(trigger));
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

    /// <summary>
    /// PUT /api/v1/schedules/{id} — updates a trigger. Any combination of name, cron expression, event
    /// name, and enabled flag can be supplied (null fields are left unchanged). Re-enabling a cron
    /// schedule recomputes its next-run time; changing the cron expression also recomputes it. 404 if
    /// the trigger doesn't exist.
    /// </summary>
    [HttpPut("schedules/{id:guid}")]
    public async Task<ActionResult<ScheduleResponse>> Update(Guid id, [FromBody] UpdateScheduleRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (await _svc.GetTriggerAsync(id, ct) is null) return NotFound();

        try
        {
            var trigger = await _svc.UpdateTriggerAsync(
                new UpdateTriggerCommand(id, request.Name, request.CronExpression, request.EventName, request.Enabled), ct);
            return Ok(ScheduleApiMapper.ToResponse(trigger));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>DELETE /api/v1/schedules/{id} — permanently removes the schedule. 204 on success, 404 if
    /// it didn't exist.</summary>
    [HttpDelete("schedules/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _svc.DeleteTriggerAsync(id, HttpContext.RequestAborted);
        return deleted ? NoContent() : NotFound();
    }
}
