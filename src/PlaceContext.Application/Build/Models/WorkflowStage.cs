using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Build;

public sealed record WorkflowStage(Guid[] JobIds, double? WaitSeconds = null, string? Condition = null,
    WorkflowStage[]? ElseBranch = null, VirtualMachineChainAction? Qemu = null, LoopChainAction? Review = null,
    SendEmailChainAction? Email = null, SendSmsChainAction? Sms = null, RepeatFlowChainAction? Repeat = null, ScoringGateChainAction? Score = null, QueryDataChainAction? Query = null)
{
    public static WorkflowStage FromView(JobChainStageView stage) => new(
        stage.Jobs.Select(x => x.JobId).ToArray(),
        (stage.Gate as WaitGateView)?.DurationSeconds, (stage.Gate as ConditionGateView)?.Expression,
        (stage.ElseBranch ?? (stage.Gate as ConditionGateView)?.ElseBranch)?.Select(FromView).ToArray(),
        stage.Action is VirtualMachineChainActionView q ? new(q.ImageProfile, q.Command, q.OutputField, q.MemoryMb, q.Cpus, q.TimeoutSeconds, q.AllowNetwork, q.PauseForLogin, q.LoginProfileId) : null,
        stage.Action is LoopChainActionView r ? new(r.Kind, r.Instructions, r.ResponseField, r.ResponseType, r.AgentProvider, r.ConnectionId, r.Model) : null,
        stage.Action is SendEmailChainActionView e ? new(e.Recipient, e.RecipientName, e.Subject, e.Body, e.AttachmentPath) : null,
        stage.Action is SendSmsChainActionView sms ? new(sms.Recipient, sms.Body) : null,
        stage.Action is RepeatFlowChainActionView repeat ? new(repeat.UntilCondition, repeat.MaxAttempts) : null,
        stage.Action is ScoringGateChainActionView score ? new(score.InputField, score.OutputField, score.Criteria, score.PassScore, score.MinimumConfidence, score.StopOnFailure) : null,
        stage.Action is QueryDataChainActionView query ? new(query.Sql, query.OutputField) : null);

    public ChainStage ToStage(int depth = 0)
    {
        if (depth > 8)
        {
            throw new ArgumentException("Workflow branches are nested too deeply.");
        }
        var actions = new ChainAction?[] { Qemu, Review, Email, Sms, Repeat, Score, Query }.OfType<ChainAction>().ToArray();
        if (actions.Length > 1 || (WaitSeconds.HasValue && Condition is not null))
        {
            throw new ArgumentException("Choose one action and one gate per stage.");
        }
        if (WaitSeconds is < 0 or > 604800 || (WaitSeconds.HasValue && !double.IsFinite(WaitSeconds.Value)))
        {
            throw new ArgumentException("Invalid wait duration.");
        }
        if (ElseBranch is { Length: > 0 } && Condition is null)
        {
            throw new ArgumentException("An else branch needs a condition.");
        }
        ChainGate? gate = WaitSeconds.HasValue ? new WaitGate(TimeSpan.FromSeconds(WaitSeconds.Value))
                    : Condition is not null ? new ConditionGate(Condition, ElseBranch?.Select(x => x.ToStage(depth + 1)).ToArray()) : null;
        return new ChainStage(JobIds, gate, action: actions.SingleOrDefault());
    }
}
