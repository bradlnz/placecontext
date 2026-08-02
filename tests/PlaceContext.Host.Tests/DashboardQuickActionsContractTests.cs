namespace PlaceContext.Host.Tests;

public sealed class DashboardQuickActionsContractTests
{
    [Fact]
    public void Dashboard_offers_job_chain_quick_runs_without_a_redundant_submit_job_action()
    {
        var page = ReadHostFile("Components", "Pages", "Dashboard.razor");

        Assert.Contains("dashboard-quick-chains", page, StringComparison.Ordinal);
        Assert.Contains("Run a job chain", page, StringComparison.Ordinal);
        Assert.Contains("ListJobChainsAsync", page, StringComparison.Ordinal);
        Assert.Contains("RunJobChainAsync", page, StringComparison.Ordinal);
        Assert.Contains("quick-chain-run", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Submit job", page, StringComparison.Ordinal);
    }

    private static string ReadHostFile(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "PlaceContext.Host"));
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
