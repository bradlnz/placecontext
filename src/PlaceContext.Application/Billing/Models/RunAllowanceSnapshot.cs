namespace PlaceContext.Application.Billing;

public sealed record RunAllowanceSnapshot(
    string Plan, string Status, long UsedRuns, DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd, int? StartsPerMinute, bool HasBillingAccount = false)
{
    public bool UnlimitedRuns => true;
    public bool CanRun => (Status is "active" or "trialing")
        && DateTimeOffset.UtcNow >= PeriodStart && DateTimeOffset.UtcNow < PeriodEnd;
}
