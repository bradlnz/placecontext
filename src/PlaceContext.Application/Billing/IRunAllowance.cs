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
        && DateTimeOffset.UtcNow >= PeriodStart && DateTimeOffset.UtcNow < PeriodEnd;
}

/// <summary>Publicly presented products. Enterprise capacity is set per contract, not by
/// a self-service checkout or an unrestricted "unlimited" entitlement.</summary>
public sealed record RunPlanTier(
    string Code, string Name, string PriceLabel, int? IncludedRuns,
    string Description, string Availability, string Support, bool SelfService,
    IReadOnlyList<string> Features);

public static class RunPlans
{
    public const string Developer = "free"; // Preserve the existing free tenant default.
    public const string Basic = "basic";
    public const string Standard = "standard";
    public const string Enterprise = "enterprise";
    public const int FreeRuns = 100;
    public const int BasicRuns = 1000;
    public const int StandardRuns = 10000;

    public static IReadOnlyList<RunPlanTier> Tiers { get; } = Array.AsReadOnly(new[]
    {
        new RunPlanTier(Developer, "Developer Edition", "$0 / month", FreeRuns,
            "Non-production sandbox for prototyping and testing.", "No uptime SLA",
            "Community support", false, new[] { "Core jobs and workflows", "Shared sandbox", "Testing and prototyping only" }),
        new RunPlanTier(Basic, "Basic Managed", "$600 / month", BasicRuns,
            "Entry-level fully managed production plan.", "99.9% SLA target, subject to agreement",
            "Standard support", true, new[] { "Managed hosting", "Workflow and job runs", "Billing and usage controls" }),
        new RunPlanTier(Standard, "Standard Managed", "$825 / month", StandardRuns,
            "More managed execution capacity for production teams.", "99.9% SLA target, subject to agreement",
            "Standard support", true, new[] { "Managed hosting", "Higher included run allowance", "Billing and usage controls" }),
        new RunPlanTier(Enterprise, "Enterprise", "From $99,000 / year", null,
            "Custom contract and run capacity; dedicated or customer-hosted deployments are scoped separately.",
            "Up to 99.99% SLA, by contract", "Dedicated support / TAM by contract",
            false, new[] { "Contract-specific run allowance", "Dedicated deployment options", "Security and compliance review" })
    });

    public static string NameFor(string plan) => plan switch
    {
        "starter" => "Basic Managed (legacy)",
        "growth" => "Standard Managed (legacy)",
        "business" => "Business (legacy)",
        _ => Tiers.FirstOrDefault(tier => tier.Code == plan)?.Name ?? plan
    };

    /// <summary>Fixed quotas for self-service products and backwards-compatible subscriptions.
    /// Enterprise deliberately has no global default: an operator must configure its signed
    /// Stripe contract and allowance for the specific tenant.</summary>
    public static int Included(string plan) => plan switch
    {
        Developer => FreeRuns,
        Basic or "starter" => BasicRuns,
        Standard or "growth" => StandardRuns,
        "business" => 50000, // Grandfathered, never offered for new checkout.
        _ => throw new ArgumentException("Unknown or contract-based run plan.", nameof(plan))
    };
}
