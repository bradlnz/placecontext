using System.Text.RegularExpressions;

namespace PlaceContext.Host.Tests;

public sealed class ScopedStatusModeStringContractTests
{
    private static readonly string[] Views =
    {
        "Components/Pages/JobChains.razor",
        "Components/Pages/Observability.razor",
        "Components/Pages/Jobs.razor",
        "Components/Shared/ParamInput.razor",
        "Components/Shared/ChainPipeline.razor",
    };

    [Fact]
    public void Scoped_views_do_not_branch_on_raw_status_or_mode_strings()
    {
        var forbidden = new Regex(
            "(?:==|!=|is\\s+(?:not\\s+)?)\\s*\\\"(?:Queued|Running|Succeeded|Failed|Partial|Skipped|Pending|details|runs|triggers|canvas|list|wait|condition|form|raw|image|code|oauth)\\\"|\\.(?:StartsWith|Contains)\\s*\\(\\s*\\\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        var violations = Views
            .SelectMany(path =>
            {
                var source = ReadScopedFile(path);
                return forbidden.Matches(source).Select(match => $"{path}: {match.Value}");
            })
            .ToArray();

        Assert.Empty(violations);
    }

    private static string ReadScopedFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "PlaceContext.Host", relativePath);
            if (File.Exists(path))
                return File.ReadAllText(path);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath}.");
    }
}
