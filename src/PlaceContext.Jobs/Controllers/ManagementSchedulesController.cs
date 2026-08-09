using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Management;

namespace PlaceContext.Jobs.Controllers;

/// <summary>Machine-facing management API for project schedules and event triggers.</summary>
[ApiController]
[Route("api/v1")]
[Authorize(
    AuthenticationSchemes = "ApiKey",
    Policy = Permission.TriggersManage)]
[Produces("application/json")]
public sealed class ManagementSchedulesController(
    IDispatcher dispatcher,
    IProjectRepository projects) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/schedules")]
    public async Task<ActionResult<IReadOnlyList<ScheduleResponse>>> ListForProject(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (await projects.GetByIdAsync(ProjectId.From(projectId), cancellationToken) is null)
            return NotFound();

        var triggers = await dispatcher.Query(new ListTriggersQuery(projectId), cancellationToken);
        return Ok(triggers.Select(ScheduleApiMapper.ToResponse).ToList());
    }

    [HttpPost("projects/{projectId:guid}/schedules")]
    public async Task<ActionResult<ScheduleResponse>> Create(
        Guid projectId,
        [FromBody] CreateScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (await projects.GetByIdAsync(ProjectId.From(projectId), cancellationToken) is null)
            return NotFound();

        var job = await dispatcher.Query(new GetJobQuery(request.JobId), cancellationToken);
        if (job is null)
            return NotFound(new { error = $"Job {request.JobId} not found." });
        if (job.ProjectId != projectId)
        {
            return BadRequest(new
            {
                error = $"Job {request.JobId} belongs to a different project.",
            });
        }

        try
        {
            var trigger = await dispatcher.Send(
                new CreateTriggerCommand(
                    request.JobId,
                    request.Name,
                    request.Kind,
                    request.CronExpression,
                    request.EventName),
                cancellationToken);
            var response = ScheduleApiMapper.ToResponse(trigger);
            return CreatedAtRoute("GetScheduleById", new { id = response.Id }, response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
