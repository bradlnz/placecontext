namespace PlaceContext.Application.Billing;

/// <summary>Host-only setting: Developer Edition is self-hosted, not an unlimited
/// free sandbox on the paid public SaaS instance.</summary>
public sealed class RunBillingDeploymentOptions
{
    public int BasicRunsPerPeriod { get; set; } = 10_000;
    public int StandardRunsPerPeriod { get; set; } = 100_000;
    public int BasicAiRequestsPerPeriod { get; set; } = 5_000;
    public int StandardAiRequestsPerPeriod { get; set; } = 50_000;

    public bool ManagedHosting
    {
        get; set;
    }
    public bool RequireExplicitTenantProvisioning
    {
        get; set;
    }
}
