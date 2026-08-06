namespace PlaceContext.Application.Ports;

public interface ICustomerPortalProvisioner
{
    Task ProvisionAsync(
        Guid tenantId,
        string slug,
        string? customDomain,
        string? brandName,
        string? brandLogoUrl,
        string? defaultPortalUserName = null,
        string? defaultPortalUserEmail = null,
        string? defaultPortalUserPassword = null,
        CancellationToken ct = default);
}
