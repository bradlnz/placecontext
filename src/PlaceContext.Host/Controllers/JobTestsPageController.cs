using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Controllers.Api.Mappers;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/test-page")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.JobsView)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class JobTestsPageController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobTestPageResponse>> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var jobsTask = placeContextService.ListJobsAsync(projectId, cancellationToken);
        var testsTask = placeContextService.ListJobTestCasesAsync(projectId, cancellationToken);
        await Task.WhenAll(jobsTask, testsTask);
        return Ok(new JobTestPageResponse(
            (await jobsTask).Select(job => new JobTestJobResponse(job.Id, job.Name)).ToList(),
            (await testsTask).Select(JobTestPageMapper.Map).ToList()));
    }

    [HttpPost("tests")]
    [Authorize(Policy = Permission.JobsEdit)]
    public Task<ActionResult<JobTestBlockResponse>> Create(
        Guid projectId,
        [FromBody] SaveJobTestBlockRequest request,
        CancellationToken cancellationToken) =>
        Save(projectId, null, request, cancellationToken);

    [HttpPut("tests/{testId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public Task<ActionResult<JobTestBlockResponse>> Update(
        Guid projectId,
        Guid testId,
        [FromBody] SaveJobTestBlockRequest request,
        CancellationToken cancellationToken) =>
        Save(projectId, testId, request, cancellationToken);

    [HttpPost("tests/{testId:guid}/run")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobTestBlockResponse>> Run(
        Guid projectId,
        Guid testId,
        CancellationToken cancellationToken)
    {
        var test = await placeContextService.GetJobTestCaseAsync(testId, cancellationToken);
        if (test is null || test.ProjectId != projectId)
            return NotFound(new { error = "The test block does not exist." });
        try
        {
            return Ok(JobTestPageMapper.Map(
                await placeContextService.RunJobTestCaseAsync(testId, cancellationToken)));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpDelete("tests/{testId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid testId,
        CancellationToken cancellationToken)
    {
        var test = await placeContextService.GetJobTestCaseAsync(testId, cancellationToken);
        if (test is null || test.ProjectId != projectId)
            return NotFound(new { error = "The test block does not exist." });
        return await placeContextService.DeleteJobTestCaseAsync(testId, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "The test block does not exist." });
    }

    private async Task<ActionResult<JobTestBlockResponse>> Save(
        Guid projectId,
        Guid? testId,
        SaveJobTestBlockRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<JobTestAssertionType>(request.AssertionType, true, out var assertion))
            return BadRequest(new { error = "Choose a valid assertion." });
        try
        {
            var test = await placeContextService.SaveJobTestCaseAsync(
                new SaveJobTestCaseCommand(
                    projectId, request.JobId, request.Name, request.InputPayload, assertion,
                    request.ExpectedValue, request.Enabled, testId),
                cancellationToken);
            return Ok(JobTestPageMapper.Map(test));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }
}
