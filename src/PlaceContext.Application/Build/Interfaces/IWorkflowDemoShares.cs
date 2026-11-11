namespace PlaceContext.Application.Build;

public interface IWorkflowDemoShares
{
    Task<WorkflowDemoLink> CreateAsync(Guid projectId, Guid runId, string outcome, int? replayStep, int lifetimeDays, CancellationToken ct = default);
    Task<DateTimeOffset?> GetExpiryAsync(Guid projectId, Guid runId, CancellationToken ct = default);
    Task RevokeAsync(Guid projectId, Guid runId, CancellationToken ct = default);
    Task<WorkflowDemo?> ResolveAsync(string token, CancellationToken ct = default);
}
