using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Jobs.Contracts.Api;

namespace PlaceContext.Jobs.Controllers;

[ApiController]
[Route("api/jobs/internal")]
[Authorize(AuthenticationSchemes = "ApiKey")]
public sealed class InternalJobsController(IDispatcher dispatcher) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/catalog")]
    public async Task<IActionResult> Catalog(Guid projectId, CancellationToken ct)
    {
        var jobs = await dispatcher.Query(new ListJobsQuery(projectId), ct);
        var chainsTask = dispatcher.Query(new ListJobChainsQuery(projectId), ct);
        var triggersTask = dispatcher.Query(new ListTriggersQuery(projectId), ct);
        var runTasks = jobs.Select(job => dispatcher.Query(new ListJobRunsQuery(job.Id), ct)).ToArray();
        await Task.WhenAll(runTasks);
        var chains = await chainsTask;
        var triggers = await triggersTask;

        return Ok(new
        {
            jobs = jobs.Select(job => new
            {
                job.Id,
                job.ProjectId,
                job.Name,
                job.Description,
                returnType = job.ReturnType.ToString(),
                job.Parameters,
            }),
            chains = chains.Select(chain => new
            {
                chain.Id,
                chain.ProjectId,
                chain.Name,
                chain.Description,
                stepCount = chain.Steps.Count,
                stages = chain.Stages.Select(stage => new
                {
                    jobs = stage.Jobs.Select(job => new { job.JobId, job.JobName }),
                    conditionExpression = stage.Gate is ConditionGateView condition
                        ? condition.Expression
                        : null,
                }),
            }),
            runs = runTasks.SelectMany(task => task.Result).Select(run => new
            {
                run.Id,
                run.JobId,
                run.Status,
                run.StartedAt,
            }),
            triggers,
        });
    }

    [HttpPost("jobs/{jobId:guid}/runs")]
    public async Task<IActionResult> RunJob(
        Guid jobId,
        InternalRunJobRequest request,
        CancellationToken ct)
    {
        var job = await dispatcher.Query(new GetJobQuery(jobId), ct);
        if (job is null || job.ProjectId != request.ProjectId)
            return NotFound(new { error = "Job not found for this project." });
        return Ok(await dispatcher.Send(new RunJobCommand(jobId, request.InputPayload), ct));
    }

    [HttpPost("chains/{chainId:guid}/runs")]
    public async Task<IActionResult> RunChain(
        Guid chainId,
        InternalRunJobChainRequest request,
        CancellationToken ct)
    {
        var chain = (await dispatcher.Query(new ListJobChainsQuery(request.ProjectId), ct))
            .FirstOrDefault(candidate => candidate.Id == chainId);
        if (chain is null) return NotFound(new { error = "Job chain not found for this project." });

        return Ok(await dispatcher.Send(
            new RunJobChainCommand(
                chainId,
                request.InputPayload,
                request.ChainRunId,
                request.StepPayloadOverrides,
                CrmClientId: request.CrmClientId),
            ct));
    }

    [HttpGet("chain-runs/{chainRunId:guid}")]
    public async Task<IActionResult> GetChainRun(Guid chainRunId, CancellationToken ct)
    {
        var run = await dispatcher.Query(new GetChainRunQuery(chainRunId), ct);
        return run is null ? NotFound() : Ok(run);
    }
}
