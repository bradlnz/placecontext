namespace PlaceContext.Application.Billing;

/// <summary>One top-level workflow execution or standalone job consumes one included run.
/// Child steps, retries, and resumptions must reuse the original billable key.</summary>
public interface IRunAllowance
{
    Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default);
    Task ReserveAsync(Guid projectId, Guid executionId, string kind, CancellationToken ct = default);
}

public sealed record RunAllowanceSnapshot(string Plan, string Status, int IncludedRuns, int UsedRuns,
    DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd)
{
    public int Remaining => Math.Max(0, IncludedRuns - UsedRuns);
    public bool CanRun => (Status is "active" or "trialing") && Remaining > 0
        && DateTimeOffset.UtcNow < PeriodEnd;
}

public static class RunPlans
{
    public const int FreeRuns = 100;
    public static int Included(string plan) => plan switch
    {
        "free" => FreeRuns,
        "starter" => 1000,
        "growth" => 10000,
        "business" => 50000,
        _ => throw new ArgumentException("Unknown run plan.", nameof(plan))
    };
}
