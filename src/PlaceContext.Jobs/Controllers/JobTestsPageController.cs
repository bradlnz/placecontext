using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Contracts.Api;
using PlaceContext.Jobs.Mapping;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs/projects/{projectId:guid}/test-page")]
[Authorize(Policy = Permission.JobsView)]
[Produces("application/json")]
public sealed class JobTestsPageController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<JobTestPageResponse>> Get(Guid projectId, CancellationToken ct)
    {
        var jobsTask = dispatcher.Query(new ListJobsQuery(projectId), ct);
        var testsTask = dispatcher.Query(new ListJobTestCasesQuery(projectId), ct);
        await Task.WhenAll(jobsTask, testsTask);
        return Ok(new JobTestPageResponse(
            (await jobsTask).Select(job => new JobTestJobResponse(job.Id, job.Name)).ToList(),
            (await testsTask).Select(JobTestPageMapper.Map).ToList()));
    }

    [HttpPost("tests")]
    [Authorize(Policy = Permission.JobsEdit)]
    public Task<ActionResult<JobTestBlockResponse>> Create(
        Guid projectId,
        SaveJobTestBlockRequest request,
        CancellationToken ct) => Save(projectId, null, request, ct);

    [HttpPut("tests/{testId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public Task<ActionResult<JobTestBlockResponse>> Update(
        Guid projectId,
        Guid testId,
        SaveJobTestBlockRequest request,
        CancellationToken ct) => Save(projectId, testId, request, ct);

    [HttpPost("tests/{testId:guid}/run")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobTestBlockResponse>> Run(
        Guid projectId,
        Guid testId,
        CancellationToken ct)
    {
        if (!await TestBelongsToProject(testId, projectId, ct))
            return NotFound(new { error = "The test block does not exist." });
        try
        {
            return Ok(JobTestPageMapper.Map(await dispatcher.Send(
                new RunJobTestCaseCommand(testId), ct)));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("tests/{testId:guid}")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<IActionResult> Delete(Guid projectId, Guid testId, CancellationToken ct)
    {
        if (!await TestBelongsToProject(testId, projectId, ct))
            return NotFound(new { error = "The test block does not exist." });
        return await dispatcher.Send(new DeleteJobTestCaseCommand(testId), ct)
            ? NoContent()
            : NotFound(new { error = "The test block does not exist." });
    }

    [HttpGet("tests/{testId:guid}/code-page")]
    [Authorize(Policy = Permission.JobsEdit)]
    public async Task<ActionResult<JobTestCodePageResponse>> GetCode(
        Guid projectId,
        Guid testId,
        CancellationToken ct)
    {
        var test = await dispatcher.Query(new GetJobTestCaseQuery(testId), ct);
        return test is null || test.ProjectId != projectId
            ? NotFound(new { error = "The test block does not exist." })
            : Ok(new JobTestCodePageResponse(JobTestPageMapper.Map(test), RuntimeOptions()));
    }

    [HttpPut("tests/{testId:guid}/code-page")]
    [Authorize(Policy = Permission.JobsEdit)]
    public Task<ActionResult<JobTestBlockResponse>> UpdateCode(
        Guid projectId,
        Guid testId,
        UpdateJobTestCodeRequest request,
        CancellationToken ct) => SaveCode(projectId, testId, request, false, ct);

    [HttpPost("tests/{testId:guid}/code-page/run")]
    [Authorize(Policy = Permission.JobsEdit)]
    public Task<ActionResult<JobTestBlockResponse>> RunCode(
        Guid projectId,
        Guid testId,
        UpdateJobTestCodeRequest request,
        CancellationToken ct) => SaveCode(projectId, testId, request, true, ct);

    private async Task<ActionResult<JobTestBlockResponse>> Save(
        Guid projectId,
        Guid? testId,
        SaveJobTestBlockRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<JobTestAssertionType>(request.AssertionType, true, out var assertion))
            return BadRequest(new { error = "Choose a valid assertion." });
        try
        {
            var test = await dispatcher.Send(new SaveJobTestCaseCommand(
                projectId,
                request.JobId,
                request.Name,
                request.InputPayload,
                assertion,
                request.ExpectedValue,
                request.Enabled,
                testId), ct);
            return Ok(JobTestPageMapper.Map(test));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private async Task<ActionResult<JobTestBlockResponse>> SaveCode(
        Guid projectId,
        Guid testId,
        UpdateJobTestCodeRequest request,
        bool run,
        CancellationToken ct)
    {
        if (!await TestBelongsToProject(testId, projectId, ct))
            return NotFound(new { error = "The test block does not exist." });
        if (!RuntimeIds.Contains(request.RuntimeId))
            return BadRequest(new { error = "Choose a supported test runtime." });
        try
        {
            var updated = await dispatcher.Send(new UpdateJobTestCodeCommand(
                testId,
                request.RuntimeId,
                request.Entrypoint,
                request.CodeFiles.Select(file => new CodeFileDto(file.Path, file.Content)).ToList(),
                false), ct);
            if (run)
                updated = await dispatcher.Send(new RunJobTestCaseCommand(testId), ct);
            return Ok(JobTestPageMapper.Map(updated));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private async Task<bool> TestBelongsToProject(Guid testId, Guid projectId, CancellationToken ct)
        => await dispatcher.Query(new GetJobTestCaseQuery(testId), ct) is { ProjectId: var owner }
            && owner == projectId;

    private static readonly string[] RuntimeIds =
        [JobTestRuntimeCatalog.Python, JobTestRuntimeCatalog.Node, JobTestRuntimeCatalog.Go, JobTestRuntimeCatalog.Ruby];

    private static IReadOnlyList<JobTestRuntimeResponse> RuntimeOptions()
        => RuntimeIds.Select(runtime =>
        {
            var starter = JobTestRuntimeCatalog.Starter(runtime);
            var files = new List<JobTestCodeFileResponse> { new(starter.Path, starter.Content) };
            if (runtime == JobTestRuntimeCatalog.Go)
                files.Add(new JobTestCodeFileResponse("go.mod", "module placecontext_tests\n\ngo 1.23\n"));
            else if (runtime == JobTestRuntimeCatalog.Python)
                files.Add(new JobTestCodeFileResponse("requirements.txt", "pytest==8.4.1\n"));
            return new JobTestRuntimeResponse(
                runtime,
                JobTestRuntimeCatalog.Label(runtime),
                JobTestFramework.Label(runtime),
                starter.Path,
                files);
        }).ToList();
}
