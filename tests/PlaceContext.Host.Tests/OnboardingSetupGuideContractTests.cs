namespace PlaceContext.Host.Tests;

public sealed class OnboardingSetupGuideContractTests
{
    [Fact]
    public void First_login_setup_focuses_on_execution_and_leaves_plans_out_of_the_portal()
    {
        var onboarding = Read("src/PlaceContext.Host/Components/Pages/Onboarding.razor");
        var cluster = Read("src/PlaceContext.Host/Components/Pages/Cluster.razor");
        var auth = Read("src/PlaceContext.Host/Controllers/AuthController.cs");
        var signup = Read("src/PlaceContext.Host/Controllers/ManagedWorkspaceSignupController.cs");

        Assert.Contains("@inject OnboardingViewModel Vm", onboarding);
        Assert.Contains("ExecutionLocation.Cloud", onboarding);
        Assert.Contains("ExecutionLocation.Local", onboarding);
        Assert.DoesNotContain("OpenSearch", onboarding);
        Assert.DoesNotContain("billing", onboarding, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("This guide does not move workloads", onboarding);
        Assert.Contains("@page \"/settings/cluster\"", cluster);
        Assert.Contains("destination == \"/\" ? \"/onboarding\"", auth);
        Assert.Contains("/onboarding", signup);
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlaceContext.slnx")))
            {
                return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
