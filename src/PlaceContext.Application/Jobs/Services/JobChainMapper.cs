using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

internal static class JobChainMapper
{
    public static async Task<JobChainView> ToViewAsync(JobChain chain, IJobRepository jobs, CancellationToken ct)
    {
        var stages = new List<JobChainStageView>(chain.Stages.Count);
        foreach (var stage in chain.Stages)
        {
            stages.Add(await ToStageViewAsync(stage, jobs, ct));
        }
        return new JobChainView(chain.Id, chain.ProjectId, chain.Name, chain.Description, stages, chain.UpdatedAt);
    }

    private static async Task<JobChainStageView> ToStageViewAsync(ChainStage stage, IJobRepository jobs, CancellationToken ct)
    {
        var stepViews = new List<JobChainStepView>(stage.JobIds.Count);
        foreach (var jobId in stage.JobIds)
        {
            var job = await jobs.GetByIdAsync(jobId, ct);
            stepViews.Add(new JobChainStepView(jobId, job?.Name ?? "(deleted)"));
        }

        IReadOnlyList<JobChainStageView>? elseBranchView = null;
        if (stage.ElseBranch is { Count: > 0 } elseBranch)
        {
            elseBranchView = await Task.WhenAll(elseBranch.Select(s => ToStageViewAsync(s, jobs, ct)));
        }

        return new JobChainStageView(stepViews, ToGateView(stage.Gate), elseBranchView);
    }

    private static ChainGateView? ToGateView(ChainGate? gate) => gate switch
    {
        null => null,
        NoGate => null,
        WaitGate w => new WaitGateView(w.Duration.TotalSeconds),
        ConditionGate c when c.ElseBranch is { Count: > 0 } eb
            => new ConditionGateView(c.Expression, null), // else-branch mapped separately on the stage
        ConditionGate c => new ConditionGateView(c.Expression, null),
        _ => null,
    };
}
