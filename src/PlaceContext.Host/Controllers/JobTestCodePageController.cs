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
[Route("api/v1/projects/{projectId:guid}/test-page/tests/{testId:guid}/code-page")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = Permission.JobsEdit)]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class JobTestCodePageController(IPlaceContextService placeContextService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobTestCodePageResponse>> Get(
        Guid projectId,
        Guid testId,
        CancellationToken cancellationToken)
    {
        var test = await placeContextService.GetJobTestCaseAsync(testId, cancellationToken);
        if (test is null || test.ProjectId != projectId)
            return NotFound(new { error = "The test block does not exist." });
        return Ok(new JobTestCodePageResponse(JobTestPageMapper.Map(test), RuntimeOptions()));
    }

    [HttpPut]
    public Task<ActionResult<JobTestBlockResponse>> Update(
        Guid projectId,
        Guid testId,
        [FromBody] UpdateJobTestCodeRequest request,
        CancellationToken cancellationToken) =>
        Save(projectId, testId, request, false, cancellationToken);

    [HttpPost("run")]
    public Task<ActionResult<JobTestBlockResponse>> Run(
        Guid projectId,
        Guid testId,
        [FromBody] UpdateJobTestCodeRequest request,
        CancellationToken cancellationToken) =>
        Save(projectId, testId, request, true, cancellationToken);

    private async Task<ActionResult<JobTestBlockResponse>> Save(
        Guid projectId,
        Guid testId,
        UpdateJobTestCodeRequest request,
        bool run,
        CancellationToken cancellationToken)
    {
        var existing = await placeContextService.GetJobTestCaseAsync(testId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
            return NotFound(new { error = "The test block does not exist." });
        if (!RuntimeIds.Contains(request.RuntimeId))
            return BadRequest(new { error = "Choose a supported test runtime." });
        try
        {
            var updated = await placeContextService.UpdateJobTestCodeAsync(
                new UpdateJobTestCodeCommand(
                    testId, request.RuntimeId, request.Entrypoint,
                    request.CodeFiles.Select(file => new CodeFileDto(file.Path, file.Content)).ToList(),
                    false),
                cancellationToken);
            if (run)
                updated = await placeContextService.RunJobTestCaseAsync(testId, cancellationToken);
            return Ok(JobTestPageMapper.Map(updated));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private static readonly string[] RuntimeIds =
        [JobTestRuntimeCatalog.Python, JobTestRuntimeCatalog.Node, JobTestRuntimeCatalog.Go, JobTestRuntimeCatalog.Ruby];

    private static IReadOnlyList<JobTestRuntimeResponse> RuntimeOptions() =>
        RuntimeIds.Select(runtime =>
        {
            var starter = JobTestRuntimeCatalog.Starter(runtime);
            var files = new List<JobTestCodeFileResponse> { new(starter.Path, starter.Content) };
            if (runtime == JobTestRuntimeCatalog.Go)
                files.Add(new("go.mod", "module placecontext_tests\n\ngo 1.23\n"));
            else if (runtime == JobTestRuntimeCatalog.Python)
                files.Add(new("requirements.txt", "pytest==8.4.1\n"));
            return new JobTestRuntimeResponse(
                runtime, JobTestRuntimeCatalog.Label(runtime), JobTestFramework.Label(runtime),
                starter.Path, files);
        }).ToList();
}
