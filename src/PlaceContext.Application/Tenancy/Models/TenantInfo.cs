namespace PlaceContext.Application.Tenancy;

/// <summary>The resolved tenant for the current request.</summary>
public sealed record TenantInfo(Guid Id, string Slug, string Name, string TimeZoneId) : ICurrentTenant
{
    public Guid TenantId => Id;
    public bool IsResolved => Id != Guid.Empty;
}
