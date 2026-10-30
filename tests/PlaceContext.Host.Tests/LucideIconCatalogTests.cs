using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class LucideIconCatalogTests
{
    [Theory]
    [InlineData("agent", "bot")]
    [InlineData("cluster", "network")]
    [InlineData("data", "database")]
    [InlineData("data.analytics", "chart-no-axes-column")]
    [InlineData("decision", "brain-circuit")]
    [InlineData("measure", "ruler")]
    [InlineData("confidence", "shield-check")]
    [InlineData("history", "rotate-ccw-clock")]
    [InlineData("phone-missed", "phone-missed")]
    [InlineData("phone-forwarded", "phone-forwarded")]
    [InlineData(" SETTINGS ", "settings-2")]
    public void Semantic_names_resolve_to_the_intended_Lucide_icon(string kind, string expected)
    {
        Assert.True(MainLayoutIconCatalog.IsKnown(kind));
        Assert.Equal(expected, MainLayoutIconCatalog.Resolve(kind));
        var svg = XElement.Parse(MainLayoutIconCatalog.Markup(kind));
        Assert.Equal(expected, svg.Attribute("data-icon")?.Value);
        Assert.Equal(MainLayoutIconCatalog.SpritePath + "#" + expected,
            Assert.Single(svg.Elements()).Attribute("href")?.Value);
    }

    [Fact]
    public void Configured_aliases_and_literal_UI_icons_have_real_bundled_geometry()
    {
        var root = FindRepoRoot();
        var sprite = XDocument.Load(Path.Combine(root, "src/PlaceContext.Host/wwwroot",
            MainLayoutIconCatalog.SpritePath.TrimStart('/')));
        XNamespace ns = "http://www.w3.org/2000/svg";
        var symbols = sprite.Root!.Elements(ns + "symbol")
            .ToDictionary(symbol => symbol.Attribute("id")!.Value, StringComparer.Ordinal);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools/icons/lucide.json")));
        foreach (var alias in config.RootElement.GetProperty("aliases").EnumerateObject())
        {
            Assert.True(MainLayoutIconCatalog.IsKnown(alias.Name), $"Unmapped alias: {alias.Name}");
            Assert.Equal(alias.Value.GetString(), MainLayoutIconCatalog.Resolve(alias.Name));
            Assert.True(symbols.TryGetValue(MainLayoutIconCatalog.Resolve(alias.Name), out var symbol),
                $"Missing SVG symbol: {alias.Name}");
            Assert.Equal("0 0 24 24", symbol!.Attribute("viewBox")?.Value);
            Assert.NotEmpty(symbol.Elements());
        }

        var components = Path.Combine(root, "src/PlaceContext.Host/Components");
        var literalIcons = new Regex("<AppIcon\\b[^>]*?\\bKind=\"(?<kind>[^\"@]+)\"",
            RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories))
        foreach (Match match in literalIcons.Matches(File.ReadAllText(file)))
        {
            var kind = match.Groups["kind"].Value;
            Assert.True(MainLayoutIconCatalog.IsKnown(kind), $"Unknown icon '{kind}' in {Path.GetFileName(file)}");
            Assert.True(symbols.ContainsKey(MainLayoutIconCatalog.Resolve(kind)), $"Missing SVG for {kind}");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-known-icon")]
    [InlineData("'><script>alert(1)</script>")]
    public void Unknown_names_render_a_safe_accessible_fallback(string? kind)
    {
        var svg = XElement.Parse(MainLayoutIconCatalog.Markup(kind, -1, "safe' onclick='alert(1)"));
        Assert.False(MainLayoutIconCatalog.IsKnown(kind));
        Assert.Equal("circle-question-mark", svg.Attribute("data-icon")?.Value);
        Assert.Equal("8", svg.Attribute("width")?.Value);
        Assert.Equal("8", svg.Attribute("height")?.Value);
        Assert.Equal("true", svg.Attribute("aria-hidden")?.Value);
        Assert.Equal("false", svg.Attribute("focusable")?.Value);
        Assert.Equal("2", svg.Attribute("stroke-width")?.Value);
        Assert.Null(svg.Attribute("onclick"));
        var use = Assert.Single(svg.Elements());
        Assert.Equal("use", use.Name.LocalName);
        Assert.Equal(MainLayoutIconCatalog.SpritePath + "#circle-question-mark", use.Attribute("href")?.Value);
        Assert.Empty(use.Elements());
    }

    [Fact]
    public void Local_sprite_and_license_match_the_pinned_source_manifest()
    {
        var vendor = Path.Combine(FindRepoRoot(), "src/PlaceContext.Host/wwwroot/vendor");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(vendor, "lucide-manifest.json")));
        var metadata = manifest.RootElement;
        var spriteBytes = File.ReadAllBytes(Path.Combine(vendor, metadata.GetProperty("asset").GetString()!));
        var licenseBytes = File.ReadAllBytes(Path.Combine(vendor, "lucide-LICENSE.txt"));
        Assert.Equal(metadata.GetProperty("sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(spriteBytes)).ToLowerInvariant());
        Assert.Equal(metadata.GetProperty("license_sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(licenseBytes)).ToLowerInvariant());

        var sprite = XDocument.Parse(System.Text.Encoding.UTF8.GetString(spriteBytes));
        var allowed = new HashSet<string>(["svg", "symbol", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"]);
        foreach (var element in sprite.Root!.DescendantsAndSelf())
        {
            Assert.Contains(element.Name.LocalName, allowed);
            Assert.DoesNotContain(element.Attributes(), attribute =>
                attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || attribute.Name.LocalName is "href" or "style");
        }
        var sourceNames = metadata.GetProperty("icons").EnumerateObject().Select(icon => icon.Name).Order().ToArray();
        var symbolNames = sprite.Root.Elements().Select(symbol => symbol.Attribute("id")!.Value).Order().ToArray();
        Assert.Equal(sourceNames, symbolNames);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlaceContext.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
