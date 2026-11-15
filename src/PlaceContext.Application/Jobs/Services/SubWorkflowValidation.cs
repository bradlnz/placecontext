using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

internal static class SubWorkflowValidation
{
    public static async Task ValidateAsync(JobChain root, IJobChainRepository chains, CancellationToken ct)
    {
        async Task Visit(JobChain workflow, HashSet<Guid> ancestors)
        {
            if (ancestors.Count >= 8 || !ancestors.Add(workflow.Id))
                throw new InvalidOperationException("Sub-workflows cannot form a cycle or nest more than eight levels.");
            var stages = new Stack<ChainStage>(workflow.Stages);
            while (stages.TryPop(out var stage))
            {
                if (workflow.Id != root.Id && (stage.Gate is WaitGate || stage.Action is RepeatFlowChainAction
                    || stage.Action is VirtualMachineChainAction { PauseForLogin: true }
                    || stage.Action is LoopChainAction loop && loop.AgentProvider is not (LoopChainAction.AutomaticAgent or LoopChainAction.JevAgent)))
                    throw new InvalidOperationException("Sub-workflows must run automatically. Put waits, repeats and requests for human input in the parent workflow.");
                foreach (var branch in stage.ElseBranch ?? (stage.Gate as ConditionGate)?.ElseBranch ?? []) stages.Push(branch);
                var childIds = stage.Action switch
                {
                    SubWorkflowChainAction child => new[] { child.WorkflowId },
                    ParallelWorkflowsChainAction parallel => parallel.Branches.Select(branch => branch.WorkflowId),
                    _ => Enumerable.Empty<Guid>()
                };
                foreach (var childId in childIds)
                {
                var target = childId == root.Id ? root : await chains.GetByIdAsync(childId, ct)
                    ?? throw new InvalidOperationException("The sub-workflow no longer exists.");
                if (target.ProjectId != root.ProjectId)
                    throw new InvalidOperationException("Sub-workflows must belong to the same project.");
                await Visit(target, new HashSet<Guid>(ancestors));
                }
            }
        }
        await Visit(root, []);
    }
}
