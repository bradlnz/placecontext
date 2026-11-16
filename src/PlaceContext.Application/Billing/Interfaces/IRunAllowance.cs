namespace PlaceContext.Application.Billing;

/// <summary>Each top-level workflow or standalone job start reserves tenant usage.
/// Managed tiers enforce period allowances and pace execution starts.
/// Workflow child jobs, retries and resumptions reuse the original key.</summary>
public interface IRunAllowance
{
    Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default);
    Task ReserveAsync(Guid projectId, Guid executionId, string kind, CancellationToken ct = default);
}
