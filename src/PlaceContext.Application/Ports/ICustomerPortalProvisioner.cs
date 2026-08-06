namespace PlaceContext.Application.Ports;

public interface ICustomerPortalProvisioner
{
    Task ProvisionAsync(
        Guid tenantId,
        string slug,
        string? customDomain,
        string? brandName,
        string? brandLogoUrl,
        CancellationToken ct = default);
}
