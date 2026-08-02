namespace PlaceContext.Host.Tests;

public sealed class OnboardingWikiNavigationContractTests
{
    [Fact]
    public void Onboarding_lives_in_the_wiki_without_a_main_menu_entry()
    {
        var menu = ReadRepositorySource("src/PlaceContext.Infrastructure/Tenancy/MenuConfigService.cs");
        var onboarding = ReadHostSource("Components/Pages/Onboarding.razor");
        var gettingStarted = ReadHostSource("Wiki/getting-started.md");

        Assert.DoesNotContain("new(\"onboarding\"", menu);
        Assert.Contains("/wiki/getting-started", onboarding);
        Assert.Contains("Connect an MCP client", gettingStarted);
        Assert.Contains("<workspace-url>/mcp", gettingStarted);
    }

    private static string ReadHostSource(string relativePath) =>
        ReadRepositorySource(Path.Combine("src/PlaceContext.Host", relativePath));

    private static string ReadRepositorySource(string relativePath)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        return File.ReadAllText(Path.Combine(repositoryRoot, relativePath));
    }
}
