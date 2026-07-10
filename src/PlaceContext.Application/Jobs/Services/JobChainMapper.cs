using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

internal static class JobChainMapper
{
    public static async Task<JobChainView> ToViewAsync(JobChain chain, IJobRepository jobs, CancellationToken ct)
    {
        var steps = new List<JobChainStepView>(chain.StepJobIds.Count);
        foreach (var jobId in chain.StepJobIds)
        {
            var job = await jobs.GetByIdAsync(jobId, ct);
            steps.Add(new JobChainStepView(jobId, job?.Name ?? "(deleted)"));
        }
        return new JobChainView(chain.Id, chain.ProjectId, chain.Name, chain.Description, steps, chain.UpdatedAt);
    }
}
