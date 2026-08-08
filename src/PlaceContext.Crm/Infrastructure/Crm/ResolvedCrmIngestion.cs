using PlaceContext.Application.Ports;

namespace PlaceContext.Crm.Infrastructure.Crm;

public sealed record ResolvedCrmIngestion(
    Guid ProjectId,
    TenantContext Tenant,
    string AllowedOrigin);
