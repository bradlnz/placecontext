namespace PlaceContext.Application.Billing;

public sealed record RunAllowanceSnapshot(
    string Plan, string Status, long UsedRuns, DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd, int? StartsPerMinute, bool HasBillingAccount = false,
    int? RunLimit = null, long UsedAiRequests = 0, int? AiRequestLimit = null)
{
    public bool UnlimitedRuns => RunLimit is null;
    public bool SubscriptionActive => (Status is "active" or "trialing")
        && DateTimeOffset.UtcNow >= PeriodStart && DateTimeOffset.UtcNow < PeriodEnd;
    public bool CanRun => SubscriptionActive && (RunLimit is null || UsedRuns < RunLimit);
}
