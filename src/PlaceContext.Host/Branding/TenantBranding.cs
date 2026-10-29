using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Host.Branding;

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

    /// <summary>
    /// Inline tenant palette inputs for #dcshell. These deliberately use dedicated variable names:
    /// the shell consumes background/panel/text only in dark mode, while the accent applies to both
    /// themes. Writing --bg/--panel/--text inline would outrank the light-theme stylesheet and make
    /// the theme switch appear broken for every workspace that has saved branding.
    /// </summary>
    public string CssOverrides()
    {
        var css = "";
        if (!string.IsNullOrWhiteSpace(BgColor)) css += $"--tenant-bg:{S(BgColor)};";
        if (!string.IsNullOrWhiteSpace(PanelColor)) css += $"--tenant-panel:{S(PanelColor)};";
        if (!string.IsNullOrWhiteSpace(TextColor)) css += $"--tenant-text:{S(TextColor)};";
        if (!string.IsNullOrWhiteSpace(AccentColor)) css += $"--tenant-accent:{S(AccentColor)};";
        return css;
    }

    // Colors land in a style attribute — allow only #hex / rgb()/hsl()-safe characters.
    private static string S(string v) => new(v.Where(c => char.IsLetterOrDigit(c)
        || c is '#' or '(' or ')' or ',' or '.' or '%' or ' ').Take(40).ToArray());
}
