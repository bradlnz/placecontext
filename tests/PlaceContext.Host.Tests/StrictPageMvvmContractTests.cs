using System.Text.RegularExpressions;

namespace PlaceContext.Host.Tests;

public sealed class StrictPageMvvmContractTests
{
    private static readonly string[] ScopedPages = EnumeratePageNames();

    [Fact]
    public void Scoped_pages_have_only_parameters_and_lifecycle_in_razor_code_blocks()
    {
        var violations = ScopedPages
            .Select(page => (page, source: ReadPage(page)))
            .SelectMany(item =>
                FindRazorCode(item.source)
                    .Where(member => !IsParameter(member) && !IsLifecycleMember(member))
                    .Select(member => $"{item.page}: {member.Trim()}")
            )
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Scoped_pages_do_not_format_values_or_call_presentation_helpers_from_razor()
    {
        var forbidden = new Regex(
            "\\.ToWorkspaceTime\\(\\)|\\.ToString\\s*\\(|FormatHelper\\.|ArtifactChart\\.|DateTimeOffset\\.(?:Now|UtcNow)"
        );
        var violations = ScopedPages
            .Select(page => (page, source: ReadPage(page)))
            .SelectMany(item =>
                forbidden.Matches(item.source).Select(match => $"{item.page}: {match.Value}")
            )
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Scoped_pages_forward_interactions_to_the_injected_view_model()
    {
        var page_lambda = new Regex("@onclick\\s*=\\s*\\\"(?![^\\\"]*\\bVm\\b)[^\\\"]*=>");
        var violations = ScopedPages
            .Select(page => (page, source: ReadPage(page)))
            .Where(item => page_lambda.IsMatch(item.source))
            .Select(item => item.page)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Pages_do_not_branch_on_raw_stringly_typed_state()
    {
        var forbidden = new Regex(
            "(?:==|!=)\\s*\"|\\bis\\s+\"|\\.(?:StartsWith|Contains)\\(\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );
        var violations = ScopedPages
            .Select(page => (page, source: ReadPage(page)))
            .SelectMany(item =>
                forbidden.Matches(item.source).Select(match => $"{item.page}: {match.Value}")
            )
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Presentation_catalog_preserves_production_formatting_and_status_semantics()
    {
        var presentation = new PlaceContext.Host.Components.ViewModels.PagePresentationCatalog();
        var timestamp = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);

        Assert.Equal("2031-02-03", presentation.Date(timestamp));
        Assert.Equal("Feb 3 · 04:05", presentation.ShortDateTime(timestamp));
        Assert.Equal("1,234", presentation.Number(1234));
        Assert.Equal("var(--good)", presentation.StatusColor("Succeeded"));
        Assert.Equal("var(--bad-bg)", presentation.StatusBackground("Failed"));
        Assert.Equal("—", presentation.StatusLabel(null));
    }

    private static bool IsParameter(string member) =>
        member.Contains("[Parameter]", StringComparison.Ordinal)
        || member.Contains("[SupplyParameterFromQuery", StringComparison.Ordinal);

    private static bool IsLifecycleMember(string member) =>
        Regex.IsMatch(
            member,
            "\\b(?:OnInitialized|OnInitializedAsync|OnParametersSet|OnParametersSetAsync|OnAfterRender|OnAfterRenderAsync|Dispose|DisposeAsync)\\b"
        );

    private static IEnumerable<string> FindRazorCode(string source)
    {
        var match = Regex.Match(source, "@code\\s*\\{(?<body>[\\s\\S]*)\\}\\s*$");
        if (!match.Success)
            return [];

        return Regex
            .Matches(
                match.Groups["body"].Value,
                "(?:(?:\\[[^]]+\\]\\s*)*(?:public|private|protected|internal)\\s+[^;{}]+(?:\\{[^{}]*\\}|=>[^;]+;|;))"
            )
            .Select(item => item.Value);
    }

    private static string[] EnumeratePageNames()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var pages = Path.Combine(
                directory.FullName,
                "src",
                "PlaceContext.Host",
                "Components",
                "Pages"
            );
            if (Directory.Exists(pages))
            {
                return Directory
                    .EnumerateFiles(pages, "*.razor", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Razor pages directory.");
    }

    private static string ReadPage(string page)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(
                directory.FullName,
                "src",
                "PlaceContext.Host",
                "Components",
                "Pages",
                $"{page}.razor"
            );
            if (File.Exists(path))
                return File.ReadAllText(path);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {page}.razor.");
    }
}
