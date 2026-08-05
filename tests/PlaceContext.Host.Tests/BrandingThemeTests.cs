using PlaceContext.Host.Branding;

namespace PlaceContext.Host.Tests;

public sealed class BrandingThemeTests
{
    [Fact]
    public void Branding_emits_theme_inputs_instead_of_overriding_active_palette_variables()
    {
        var branding = new TenantBranding(
            BgColor: "#101112",
            PanelColor: "#202122",
            TextColor: "#f0f1f2",
            AccentColor: "#3366cc"
        );

        var css = branding.CssOverrides();

        Assert.Contains("--tenant-bg:#101112;", css);
        Assert.Contains("--tenant-panel:#202122;", css);
        Assert.Contains("--tenant-text:#f0f1f2;", css);
        Assert.Contains("--tenant-accent:#3366cc;", css);
        Assert.DoesNotContain("--bg:", css);
        Assert.DoesNotContain("--panel:", css);
        Assert.DoesNotContain("--text:", css);
        Assert.DoesNotContain("--brand:", css);
    }
}
