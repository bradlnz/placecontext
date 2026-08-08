namespace PlaceContext.Infrastructure.Tenancy;

internal sealed record MenuCatalogItem(
    string Id,
    string DefaultLabel,
    string Kind,
    string? HrefTemplate,
    string? RequiredPermission,
    string? Icon,
    string? DefaultSection,
    string? Parent = null);
