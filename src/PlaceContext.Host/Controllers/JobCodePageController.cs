using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Management;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/job-page/jobs/{jobId:guid}/code-page")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.JobsEdit)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class JobCodePageController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobCodePageResponse>> Get(
        Guid projectId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await placeContextService.GetJobAsync(jobId, cancellationToken);
        return job is null || job.ProjectId != projectId
            ? NotFound(new { error = "The job does not exist." })
            : Ok(new JobCodePageResponse(JobApiMapper.ToResponse(job)));
    }

    [HttpPut]
    public Task<ActionResult<JobResponse>> Update(
        Guid projectId, Guid jobId, [FromBody] UpdateJobCodePageRequest request,
        CancellationToken cancellationToken) => Save(projectId, jobId, request, cancellationToken);

    [HttpPost("run")]
    [Authorize(Policy = Permission.JobsManage)]
    public async Task<ActionResult<RunJobCodePageResponse>> Run(
        Guid projectId, Guid jobId, [FromBody] UpdateJobCodePageRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await Save(projectId, jobId, request, cancellationToken);
        if (saved.Result is not null) return saved.Result;
        try
        {
            var job = saved.Value!;
            var run = await placeContextService.RunJobAsync(jobId, ct: cancellationToken);
            return Ok(new RunJobCodePageResponse(job, JobsPageController.MapDetail(run)));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private async Task<ActionResult<JobResponse>> Save(
        Guid projectId, Guid jobId, UpdateJobCodePageRequest request,
        CancellationToken cancellationToken)
    {
        var job = await placeContextService.GetJobAsync(jobId, cancellationToken);
        if (job is null || job.ProjectId != projectId)
            return NotFound(new { error = "The job does not exist." });
        try
        {
            var updated = await placeContextService.UploadJobCodeAsync(new UploadJobCodeCommand(
                jobId, projectId, job.Name, request.RuntimeId, request.Entrypoint,
                request.Files.Select(file => new CodeFileDto(file.Path, file.Content)).ToList()),
                cancellationToken);
            return JobApiMapper.ToResponse(updated);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }
}
