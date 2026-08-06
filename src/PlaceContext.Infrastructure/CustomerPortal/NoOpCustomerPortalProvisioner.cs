using System;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.CustomerPortal;

public sealed class NoOpCustomerPortalProvisioner : ICustomerPortalProvisioner
{
    public Task ProvisionAsync(
        Guid tenantId,
        string slug,
        string? customDomain,
        string? brandName,
        string? brandLogoUrl,
        CancellationToken ct = default)
    {
        _ = tenantId;
        _ = slug;
        _ = customDomain;
        _ = brandName;
        _ = brandLogoUrl;
        _ = ct;
        throw new InvalidOperationException(
            "Customer portal provisioning is unavailable in this environment. Deploy PlaceContext in Kubernetes to provision portal routes and pods.");
    }
}
