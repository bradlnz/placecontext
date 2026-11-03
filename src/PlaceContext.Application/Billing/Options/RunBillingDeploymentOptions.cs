namespace PlaceContext.Application.Billing;

/// <summary>Host-only setting: Developer Edition is self-hosted, not an unlimited
/// free sandbox on the paid public SaaS instance.</summary>
public sealed class RunBillingDeploymentOptions
{
    public bool ManagedHosting
    {
        get; set;
    }
    public bool RequireExplicitTenantProvisioning
    {
        get; set;
    }
}
