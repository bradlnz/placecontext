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

/// <summary>Machine-facing management API for project job definitions.</summary>
[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = "ApiKey")]
[Produces("application/json")]
public sealed class ManagementJobsController(
    IDispatcher dispatcher,
    IProjectRepository projects) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsView)]
    public async Task<ActionResult<IReadOnlyList<JobResponse>>> ListForProject(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (await projects.GetByIdAsync(ProjectId.From(projectId), cancellationToken) is null)
            return NotFound();

        var jobs = await dispatcher.Query(new ListJobsQuery(projectId), cancellationToken);
        return Ok(jobs.Select(JobApiMapper.ToResponse).ToList());
    }

    [HttpPost("projects/{projectId:guid}/jobs")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobResponse>> Create(
        Guid projectId,
        [FromBody] JobRequest request,
        CancellationToken cancellationToken)
    {
        if (await projects.GetByIdAsync(ProjectId.From(projectId), cancellationToken) is null)
            return NotFound();

        try
        {
            var job = await dispatcher.Send(
                JobApiMapper.ToCreateCommand(projectId, request),
                cancellationToken);
            var response = JobApiMapper.ToResponse(job);
            return CreatedAtRoute("GetJobById", new { id = response.Id }, response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
