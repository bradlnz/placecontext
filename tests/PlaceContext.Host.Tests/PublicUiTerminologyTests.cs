using System.Text.RegularExpressions;

namespace PlaceContext.Host.Tests;

/// <summary>Keep product copy provider-neutral while preserving the internal integration contract.</summary>
public sealed class PublicUiTerminologyTests
{
    [Fact]
    public void Customer_facing_views_and_help_do_not_name_the_backend_verification_provider()
    {
        var host = FindHostRoot();
        var files = Directory.EnumerateFiles(Path.Combine(host, "Components"), "*.razor",
                SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(host, "Wiki"), "*.md",
                SearchOption.AllDirectories))
            .Concat(new[]
            {
                "Components/ViewModels/BuildChatViewModel.cs",
                "Components/ViewModels/AgentWorkflowsViewModel.cs",
                "Components/ViewModels/PersonalDashboardsViewModel.cs",
                "Components/ViewModels/PersonalDashboardsViewModel.Decisions.cs",
                "Build/BuildChatAgent.cs"
            }.Select(name => Path.Combine(host, name)));
        // JevAgent and IJevReviewer are internal code identifiers, not displayed copy;
        // standalone vendor names in labels, help text or system prompts are disallowed.
        var branded = new Regex(@"(?i)(?<![A-Za-z0-9_])(?:jev|typesafe)(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
        var failures = files.Select(path => new
            {
                Path = Path.GetRelativePath(host, path),
                Matches = branded.Matches(File.ReadAllText(path))
            })
            .Where(x => x.Matches.Count > 0)
            .Select(x => x.Path)
            .ToArray();
        Assert.True(failures.Length == 0,
            "Provider brand leaked into customer-facing copy: " + string.Join(", ", failures));
    }

    private static string FindHostRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var host = Path.Combine(current.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host)) return host;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("PlaceContext.Host root not found.");
    }
}
