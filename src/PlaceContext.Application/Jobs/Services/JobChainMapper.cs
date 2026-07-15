using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class JobChainMapper
{
    public static async Task<JobChainView> ToViewAsync(JobChain chain, IJobRepository jobs, CancellationToken ct)
    {
        var stages = new List<JobChainStageView>(chain.Stages.Count);
        foreach (var stage in chain.Stages)
        {
            var stepViews = new List<JobChainStepView>(stage.JobIds.Count);
            foreach (var jobId in stage.JobIds)
            {
                var job = await jobs.GetByIdAsync(jobId, ct);
                stepViews.Add(new JobChainStepView(jobId, job?.Name ?? "(deleted)"));
            }
            stages.Add(new JobChainStageView(stepViews));
        }
        return new JobChainView(chain.Id, chain.ProjectId, chain.Name, chain.Description, stages, chain.UpdatedAt);
    }
}
