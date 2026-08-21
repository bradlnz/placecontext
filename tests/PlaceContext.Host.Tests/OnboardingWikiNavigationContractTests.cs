namespace PlaceContext.Host.Tests;

public sealed class OnboardingSetupGuideContractTests
{
    [Fact]
    public void First_login_onboarding_is_a_data_source_and_opensearch_guide()
    {
        var menu = ReadRepositorySource(
            "src/PlaceContext.Infrastructure/Tenancy/MenuConfigService.cs"
        );
        var onboarding = ReadHostSource("Components/Pages/Onboarding.razor");
        var onboardingViewModel = ReadHostSource("Components/ViewModels/OnboardingViewModel.cs");
        var auth = ReadHostSource("Controllers/AuthController.cs");

        Assert.DoesNotContain("new(\"onboarding\"", menu);
        Assert.Contains("@inject OnboardingViewModel Vm", onboarding);
        Assert.Contains("Choose a data source", onboarding);
        Assert.Contains("Do you need OpenSearch?", onboarding);
        Assert.Contains("DataSourceOptions", onboardingViewModel);
        Assert.Contains("ListProjectDataTablesAsync", onboardingViewModel);
        Assert.Contains("ListMcpConnectionsAsync", onboardingViewModel);
        Assert.Contains("PlaceContext:Ingest:Key", onboardingViewModel);
        Assert.Contains("IOpenSearchConnectionResolver", onboardingViewModel);
        Assert.Contains("destination == \"/\" ? \"/onboarding\"", auth);
    }

    private static string ReadHostSource(string relativePath) =>
        ReadRepositorySource(Path.Combine("src/PlaceContext.Host", relativePath));

    private static string ReadRepositorySource(string relativePath)
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../..")
        );
        return File.ReadAllText(Path.Combine(repositoryRoot, relativePath));
    }
}
