namespace PlaceContext.Application.Features;

public sealed record CrmClientView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Company,
    string? Email,
    string? Phone,
    string LifecycleStage,
    string? Notes,
    bool CustomerPortalEnabled,
    string? CustomerPortalSlug,
    string? CustomerPortalDomain,
    string? CustomerPortalBrandName,
    string? CustomerPortalLogoUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
