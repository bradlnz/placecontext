using System.Collections.Immutable;
using System.Reflection;
using Markdig;

namespace PlaceContext.App.Wiki;

/// <summary>
/// Static platform documentation, parsed once into an ordered rendered table of contents.
/// </summary>
public static class WikiLibrary
{
    private static readonly string[] Order =
    {
        "getting-started", "setup", "projects", "crm-and-communications", "jobs-and-artifacts",
        "events-and-schedules", "charts-and-reports", "project-data", "entities-and-insights",
        "security-and-sharing", "cluster-and-nodes", "agent-chat", "mcp-and-agents",
    };

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().UseSoftlineBreakAsHardlineBreak().Build();

    public static ImmutableArray<WikiArticle> Articles { get; } = Load();

    public static WikiArticle? Find(string? slug) =>
        Articles.FirstOrDefault(article =>
            string.Equals(article.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private static ImmutableArray<WikiArticle> Load()
    {
        var assembly = typeof(WikiLibrary).Assembly;
        var articles = new List<WikiArticle>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

            var slug = SlugFromResource(resource);
            var markdown = ReadResource(assembly, resource);
            var (title, summary) = ParseHeader(markdown, slug);
            articles.Add(new WikiArticle(
                slug,
                title,
                summary,
                Markdown.ToHtml(markdown, Pipeline)));
        }

        return articles
            .OrderBy(article => IndexOf(article.Slug))
            .ThenBy(article => article.Title, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static string SlugFromResource(string resource)
    {
        var name = resource[..^3];
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static string ReadResource(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static (string Title, string Summary) ParseHeader(string markdown, string slug)
    {
        var title = slug.Replace('-', ' ');
        var summary = string.Empty;
        var lines = markdown.Split('\n');
        var index = 0;
        for (; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("# ", StringComparison.Ordinal)) continue;
            title = line[2..].Trim();
            index++;
            break;
        }

        for (; index < lines.Length; index++)
        {
            var line = lines[index].Trim().Trim('*', '_').Trim();
            if (line.Length == 0) continue;
            summary = line;
            break;
        }

        return (title, summary);
    }

    private static int IndexOf(string slug)
    {
        var index = Array.IndexOf(Order, slug);
        return index < 0 ? int.MaxValue : index;
    }
}
