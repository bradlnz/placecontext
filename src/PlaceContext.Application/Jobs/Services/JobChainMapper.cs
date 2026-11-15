using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

internal static class JobChainMapper
{
    public static async Task<JobChainView> ToViewAsync(JobChain chain, IJobRepository jobs, CancellationToken ct)
        => (await ToViewsAsync([chain], jobs, ct))[0];

    public static async Task<IReadOnlyList<JobChainView>> ToViewsAsync(
        IReadOnlyList<JobChain> chains, IJobRepository jobs, CancellationToken ct)
    {
        var ids = chains.SelectMany(chain => JobIds(chain.Stages)).Distinct().ToArray();
        var names = ids.Length == 0
            ? new Dictionary<Guid, string>()
            : await jobs.GetNamesAsync(ids, ct);
        return chains.Select(chain => new JobChainView(chain.Id, chain.ProjectId, chain.Name,
            chain.Description, chain.Stages.Select(stage => ToStageView(stage, names)).ToList(), chain.UpdatedAt)).ToList();
    }

    private static IEnumerable<Guid> JobIds(IReadOnlyList<ChainStage> stages)
        => stages.SelectMany(stage => stage.JobIds.Concat(JobIds(ElseBranch(stage) ?? [])));

    private static IReadOnlyList<ChainStage>? ElseBranch(ChainStage stage)
        => stage.ElseBranch ?? (stage.Gate as ConditionGate)?.ElseBranch;

    private static JobChainStageView ToStageView(ChainStage stage, IReadOnlyDictionary<Guid, string> names)
        => new(stage.JobIds.Select(id => new JobChainStepView(id, names.GetValueOrDefault(id, "(deleted)"))).ToList(),
            ToGateView(stage.Gate),
            ElseBranch(stage) is { Count: > 0 } branches
                ? branches.Select(branch => ToStageView(branch, names)).ToList()
                : null,
            ToActionView(stage.Action));

    private static ChainActionView? ToActionView(ChainAction? action) => action switch
    {
        null => null,
        ParallelWorkflowsChainAction parallel => new ParallelWorkflowsChainActionView(parallel.Branches, parallel.OutputField),
        SubWorkflowChainAction child => new SubWorkflowChainActionView(child.WorkflowId, child.WorkflowName),
        ContainerBuildChainAction build => new ContainerBuildChainActionView(build.Dockerfile, build.ContextField, build.OutputField, build.TimeoutSeconds, build.AllowNetwork),
        ContainerRunChainAction container => new ContainerRunChainActionView(container.Image, container.ImageField, container.OutputField, container.Command, container.Port, container.MemoryMb, container.Cpus, container.TimeoutSeconds, container.AllowNetwork),
        VirtualMachineChainAction vm => new VirtualMachineChainActionView(vm.ImageProfile, vm.Command, vm.OutputField, vm.MemoryMb, vm.Cpus, vm.TimeoutSeconds, vm.AllowNetwork, vm.PauseForLogin, vm.LoginProfileId),
        QueryDataChainAction query => new QueryDataChainActionView(query.Sql, query.OutputField),
        ScoringGateChainAction score => new ScoringGateChainActionView(score.InputField, score.OutputField, score.Criteria, score.PassScore, score.MinimumConfidence, score.StopOnFailure),
        RepeatFlowChainAction repeat => new RepeatFlowChainActionView(repeat.UntilCondition, repeat.MaxAttempts),
        LoopChainAction loop => new LoopChainActionView(loop.Type, loop.Instructions, loop.ResponseField, loop.ResponseType, loop.AgentProvider, loop.ConnectionId, loop.Model, loop.AgentId),
        SendEmailChainAction email => new SendEmailChainActionView(
            email.Recipient, email.RecipientName, email.Subject, email.Body, email.AttachmentPath),
        SendSmsChainAction sms => new SendSmsChainActionView(sms.Recipient, sms.Body),
        _ => throw new InvalidOperationException($"Unsupported chain action '{action.Type}'."),
    };

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
