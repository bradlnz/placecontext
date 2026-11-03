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
        VirtualMachineChainAction vm => new VirtualMachineChainActionView(vm.ImageProfile, vm.Command, vm.OutputField, vm.MemoryMb, vm.Cpus, vm.TimeoutSeconds, vm.AllowNetwork, vm.PauseForLogin, vm.LoginProfileId),
        LoopChainAction loop => new LoopChainActionView(loop.Type, loop.Instructions, loop.ResponseField, loop.ResponseType, loop.AgentProvider),
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
