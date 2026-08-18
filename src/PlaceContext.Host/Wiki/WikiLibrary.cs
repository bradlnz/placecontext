using System.Collections.Immutable;
using System.Reflection;
using Markdig;

namespace PlaceContext.Host.Wiki;

/// <summary>One wiki article: its url slug, display title, one-line summary, and rendered HTML.</summary>
public sealed record WikiArticle(string Slug, string Title, string Summary, string Html);

/// <summary>
/// The platform wiki: markdown files embedded from the Wiki/ folder at build time (see the csproj
/// EmbeddedResource glob), parsed once into an ordered, rendered table of contents. Content-only —
/// no data access — so it's a plain singleton, available before any tenant/login.
/// </summary>
public static class WikiLibrary
{
    // Reading order for the nav — files not listed here fall to the end, alphabetically.
    private static readonly string[] Order =
    {
        "getting-started", "setup", "opensearch-integration", "sso-and-oauth",
        "projects", "jobs-and-artifacts",
        "events-and-schedules", "charts-and-reports", "project-data", "entities-and-insights",
        "security-and-sharing", "cluster-and-nodes", "agent-chat", "mcp-and-agents",
    };

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().UseSoftlineBreakAsHardlineBreak().Build();

    public static ImmutableArray<WikiArticle> Articles { get; } = Load();

    public static WikiArticle? Find(string? slug) =>
        Articles.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private static ImmutableArray<WikiArticle> Load()
    {
        var asm = typeof(WikiLibrary).Assembly;
        var articles = new List<WikiArticle>();

        foreach (var resource in asm.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            var slug = SlugFromResource(resource);
            var markdown = ReadResource(asm, resource);
            var (title, summary) = ParseHeader(markdown, slug);
            var html = Markdown.ToHtml(markdown, Pipeline);
            articles.Add(new WikiArticle(slug, title, summary, html));
        }

        return articles
            .OrderBy(a => IndexOf(a.Slug))
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    // "PlaceContext.Host.Wiki.getting-started.md" → "getting-started"
    private static string SlugFromResource(string resource)
    {
        var name = resource[..^3];                    // strip ".md"
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static string ReadResource(Assembly asm, string resource)
    {
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // First "# H1" is the title; the first non-empty line after it (stripped of markdown emphasis)
    // is the summary. Falls back to the slug when the file has no header.
    private static (string Title, string Summary) ParseHeader(string markdown, string slug)
    {
        string title = slug.Replace('-', ' ');
        string summary = "";
        var lines = markdown.Split('\n');
        var i = 0;
        for (; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                title = line[2..].Trim();
                i++;
                break;
            }
        }
        for (; i < lines.Length; i++)
        {
            var line = lines[i].Trim().Trim('*', '_').Trim();
            if (line.Length > 0) { summary = line; break; }
        }
        return (title, summary);
    }

    private static int IndexOf(string slug)
    {
        var idx = Array.IndexOf(Order, slug);
        return idx < 0 ? int.MaxValue : idx;
    }
}
