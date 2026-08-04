namespace PlaceContext.Host.Tests;

public sealed class DashboardQuickActionsContractTests
{
    [Fact]
    public void Dashboard_offers_job_chain_quick_runs_without_a_redundant_submit_job_action()
    {
        var page = ReadHostFile("Components", "Pages", "Dashboard.razor");
        var css = ReadHostFile("Components", "Pages", "Dashboard.razor.css");
        var viewModel = ReadHostFile("Components", "ViewModels", "DashboardViewModel.cs");
        var prompt = ReadHostFile(
            "Components",
            "ViewModels",
            "_Shared",
            "Helpers",
            "ParameterPromptState.cs"
        );

        Assert.Contains("dashboard-quick-chains", page, StringComparison.Ordinal);
        Assert.Contains("Run a job chain", page, StringComparison.Ordinal);
        Assert.Contains("ListJobChainsAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("ListJobsAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("ParameterPromptState", page, StringComparison.Ordinal);
        Assert.Contains("RunPromptSteps", page, StringComparison.Ordinal);
        Assert.Contains("ParamInput", page, StringComparison.Ordinal);
        Assert.Contains("ToStepPayloadOverrides", prompt, StringComparison.Ordinal);
        Assert.Contains("PrepareQuickChainRunAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("quick-chain-run", page, StringComparison.Ordinal);
        Assert.Contains("padding: 16px", css, StringComparison.Ordinal);
        Assert.Contains("quick-chain-modal", css, StringComparison.Ordinal);
        Assert.DoesNotContain("Submit job", page, StringComparison.Ordinal);
    }

    private static string ReadHostFile(params string[] parts)
    {
        var root = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "PlaceContext.Host"
            )
        );
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
