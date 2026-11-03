namespace PlaceContext.Application.Billing;

/// <summary>Every edition has unlimited run starts. Paid plans differ by
/// managed admission throughput; the rate is not a CPU/concurrency guarantee.</summary>
public static class RunPlans
{
    public const string Developer = "free";
    public const string Basic = "basic";
    public const string Standard = "standard";
    public const string Enterprise = "enterprise";
    public const int Unlimited = 0; // Legacy IncludedRuns column.
    public const int FreeRuns = Unlimited; // Compatibility with original migration.

    public const int BasicStartsPerMinute = 60;
    public const int StandardStartsPerMinute = 600;
    // Cluster-wide top-level workload concurrency, not a volume limit.
    public const int BasicConcurrentRuns = 2;
    public const int StandardConcurrentRuns = 8;

    public static IReadOnlyList<RunPlanTier> Tiers
    {
        get;
    } = Array.AsReadOnly(new[]
    {
        new RunPlanTier(Developer, "Developer Edition", "Free",
            "Self-hosted development and prototyping. Bring your own compute.",
            "Self-hosted; no managed SLA", "Community support", false, null,
            new[] { "Unlimited workflow and job starts", "Runs on your own infrastructure",
                "Non-production edition" }),
        new RunPlanTier(Basic, "Basic Managed", "$600 / month",
            "Managed workflows with a lower start rate; excess starts wait their turn.",
            "99.9% SLA target, subject to agreement", "Standard support", true,
            BasicStartsPerMinute, new[] { "Unlimited workflow and job starts",
                "2 concurrent top-level executions", "Shared managed execution", "Usage history and billing controls" }),
        new RunPlanTier(Standard, "Standard Managed", "$825 / month",
            "Managed workflows with higher start throughput.",
            "99.9% SLA target, subject to agreement", "Standard support", true,
            StandardStartsPerMinute, new[] { "Unlimited workflow and job starts",
                "8 concurrent top-level executions", "Higher managed start throughput", "Usage history and billing controls" }),
        new RunPlanTier(Enterprise, "Enterprise", "From $99,000 / year",
            "Dedicated or customer-hosted deployment options are scoped by contract.",
            "Up to 99.99% SLA, by contract", "Dedicated support / TAM by contract",
            false, null, new[] { "Unlimited workflow and job starts",
                "Contract-specific admission throughput", "Dedicated deployment options" })
    });

    public static string NameFor(string plan) => plan switch
    {
        "starter" => "Basic Managed (legacy)",
        "growth" => "Standard Managed (legacy)",
        "business" => "Business (legacy)",
        _ => Tiers.FirstOrDefault(tier => tier.Code == plan)?.Name ?? plan
    };

    public static int ConcurrentRuns(string plan) => plan switch
    {
        Developer => 0,
        Basic or "starter" => BasicConcurrentRuns,
        Standard or "growth" => StandardConcurrentRuns,
        "business" => 16,
        _ => throw new ArgumentException("Unknown or contract-controlled run plan.", nameof(plan))
    };

    public static int StartsPerMinute(string plan) => plan switch
    {
        Developer => 0,
        Basic or "starter" => BasicStartsPerMinute,
        Standard or "growth" => StandardStartsPerMinute,
        "business" => 1200,
        _ => throw new ArgumentException("Unknown or contract-controlled run plan.", nameof(plan))
    };

    /// <summary>Compatibility: a zero allowance means unlimited, not exhausted.</summary>
    public static int Included(string plan) => plan switch
    {
        Developer or Basic or Standard or Enterprise or "starter" or "growth" or "business" => Unlimited,
        _ => throw new ArgumentException("Unknown run plan.", nameof(plan))
    };
}
