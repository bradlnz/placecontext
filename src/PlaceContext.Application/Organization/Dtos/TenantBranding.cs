namespace PlaceContext.Application.Dtos;

/// <summary>
/// Whitelabel branding for the current tenant: a product name, an inline logo, and the shell's
/// core colors. Stored as one JSON blob on the tenant row; empty/null fields fall back to the
/// built-in placecontext look, so a partial brand never breaks the theme.
/// </summary>
public sealed record TenantBranding(
    string? ProductName = null,
    string? LogoDataUri = null,
    string? BgColor = null,
    string? PanelColor = null,
    string? TextColor = null,
    string? AccentColor = null)
{
    public bool IsDefault => this == new TenantBranding();

}
