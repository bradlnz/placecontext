namespace PlaceContext.Application.Dashboards;

/// <summary>
/// Pending actions from real, durable workflow human-input checkpoints. An assessment from Jev
/// is evidence, never an approval or permission to continue execution.
/// </summary>
public interface IWorkflowActionInbox
{
    Task<IReadOnlyList<WorkflowApprovalAction>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task RespondAsync(Guid projectId, Guid chainRunId, Guid requestId, bool approved, CancellationToken ct = default);
}

public sealed record WorkflowApprovalAction(
    Guid ChainRunId,
    Guid ChainId,
    Guid RequestId,
    string WorkflowName,
    string Instructions,
    DateTimeOffset StartedAt,
    int StepIndex,
    string ResponseField)
{
    public string ActionId => $"{ChainRunId:N}:{RequestId:N}";
    // A generic yes/no checkpoint isn't automatically an approval question.
    public bool IsApprovalCheckpoint => ResponseField.Contains("approv", StringComparison.OrdinalIgnoreCase)
        || Instructions.Contains("approve", StringComparison.OrdinalIgnoreCase)
        || Instructions.Contains("approval", StringComparison.OrdinalIgnoreCase);
}
