using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;

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
        var runTasks = jobs.Select(job => dispatcher.Query(new ListJobRunsQuery(job.Id), ct)).ToArray();
        await Task.WhenAll(runTasks);
        var chains = await chainsTask;

        return Ok(new
        {
            jobs = jobs.Select(job => new
            {
                job.Id,
                job.ProjectId,
                job.Name,
                job.Description,
                returnType = job.ReturnType.ToString(),
            }),
            chains = chains.Select(chain => new
            {
                chain.Id,
                chain.ProjectId,
                chain.Name,
                chain.Description,
                stages = chain.Stages.Select(stage => new
                {
                    jobIds = stage.Jobs.Select(job => job.JobId),
                }),
            }),
            runs = runTasks.SelectMany(task => task.Result).Select(run => new
            {
                run.Id,
                run.JobId,
                run.Status,
                run.StartedAt,
            }),
        });
    }
}
