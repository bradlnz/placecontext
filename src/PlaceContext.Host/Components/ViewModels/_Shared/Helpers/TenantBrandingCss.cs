using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

public static class TenantBrandingCss
{
    /// <summary>
    /// Inline tenant palette inputs for #dcshell. These deliberately use dedicated variable names:
    /// the shell consumes background/panel/text only in dark mode, while the accent applies to both
    /// themes. Writing --bg/--panel/--text inline would outrank the light-theme stylesheet and make
    /// the theme switch appear broken for every workspace that has saved branding.
    /// </summary>
    public static string CssOverrides(this TenantBranding branding)
    {
        var css = "";
        if (!string.IsNullOrWhiteSpace(branding.BgColor))
        {
            css += $"--tenant-bg:{SafeColor(branding.BgColor)};";
        }
        if (!string.IsNullOrWhiteSpace(branding.PanelColor))
        {
            css += $"--tenant-panel:{SafeColor(branding.PanelColor)};";
        }
        if (!string.IsNullOrWhiteSpace(branding.TextColor))
        {
            css += $"--tenant-text:{SafeColor(branding.TextColor)};";
        }
        if (!string.IsNullOrWhiteSpace(branding.AccentColor))
        {
            css += $"--tenant-accent:{SafeColor(branding.AccentColor)};";
        }
        return css;
    }

    // Colors land in a style attribute — allow only #hex / rgb()/hsl()-safe characters.
    private static string SafeColor(string v) => new(v.Where(c => char.IsLetterOrDigit(c)
        || c is '#' or '(' or ')' or ',' or '.' or '%' or ' ').Take(40).ToArray());
}
