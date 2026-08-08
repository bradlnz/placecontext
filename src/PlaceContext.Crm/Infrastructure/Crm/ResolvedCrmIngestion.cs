using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Crm.Infrastructure.Crm;

public sealed record ResolvedCrmIngestion(
    Guid ProjectId,
    TenantInfo Tenant,
    string AllowedOrigin);
