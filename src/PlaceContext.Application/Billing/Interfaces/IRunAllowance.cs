namespace PlaceContext.Application.Billing;

/// <summary>Each top-level workflow or standalone job start is metered for reporting.
/// Counts are unlimited; managed tiers pace execution starts instead of rejecting volume.
/// Workflow child jobs, retries and resumptions reuse the original key.</summary>
public interface IRunAllowance
{
    Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default);
    Task ReserveAsync(Guid projectId, Guid executionId, string kind, CancellationToken ct = default);
}
