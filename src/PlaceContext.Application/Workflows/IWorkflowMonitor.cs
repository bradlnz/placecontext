namespace PlaceContext.Application.Workflows;

/// <summary>
/// Bounded operational snapshot backed by persisted workflow runs. No inferred success,
/// population coverage, confidence scores or customer identities.
/// </summary>
public sealed record WorkflowMonitorSnapshot(
    DateTimeOffset LoadedAt,
    int WorkflowCount,
    int WorkflowsScanned,
    int PerWorkflowRunLimit,
    IReadOnlyList<WorkflowRunIndicator> Runs,
    IReadOnlyList<WorkflowHumanAction> Actions,
    bool CanRespond)
{
    public int RunningCount => Runs.Count(x => x.Status is "Running" or "Waiting");
    public int FailedCount => Runs.Count(x => x.Status is "Failed" or "Partial");
    public int SucceededCount => Runs.Count(x => x.Status == "Succeeded");
    public int CompletedCount => Runs.Count(x => x.Status is "Succeeded" or "Failed" or "Partial" or "Cancelled");
    public int? SuccessPercent => CompletedCount == 0 ? null :
        (int)Math.Round(100d * SucceededCount / CompletedCount, MidpointRounding.AwayFromZero);
}

public sealed record WorkflowRunIndicator(
    Guid RunId, Guid WorkflowId, string WorkflowName, string Status,
    int CompletedSteps, int TotalSteps, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);

public sealed record WorkflowHumanAction(
    Guid RunId, Guid WorkflowId, Guid RequestId, string WorkflowName, string Instructions,
    DateTimeOffset StartedAt, int StepIndex, string? ResponseField,
    bool CanAnswerYesNo, bool IsApproval)
{
    public string Key => $"{RunId:N}:{RequestId:N}";
}

public interface IWorkflowMonitor
{
    Task<WorkflowMonitorSnapshot> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task RespondAsync(Guid projectId, Guid runId, Guid requestId,
        bool approved, CancellationToken cancellationToken = default);
}
