using System.Text.RegularExpressions;
using PlaceContext.Application.Access;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowInputNotificationContractTests
{
    [Fact]
    public void Workflow_notification_opens_the_paused_run_in_existing_workflows_page()
    {
        var host = FindHostRoot();
        var razor = File.ReadAllText(Path.Combine(host, "Components", "Pages", "JobChains.razor"));
        var viewModel = File.ReadAllText(Path.Combine(host, "Components", "ViewModels", "JobChains", "JobChainsViewModel.cs"));
        Assert.Contains("[SupplyParameterFromQuery(Name = \"run\")]", razor);
        Assert.Contains("Vm.LoadPageAsync(ProjectId, WorkflowId ?? ChainId, RunId)", razor);
        Assert.Contains("run.ProjectId != projectId || run.ChainId != id", viewModel);
        Assert.Contains("await OpenChainRunAsync(run);", viewModel);
        Assert.Contains("workflow-input-banner", razor);
        Assert.Contains("Vm.RunsAwaitingInput", razor);
    }

    [Fact]
    public void Bell_notifies_needs_input_without_exposing_agent_or_data_store_sections()
    {
        var host = FindHostRoot();
        var bell = File.ReadAllText(Path.Combine(host, "Components", "Shared", "NotificationsBell.razor"));
        var vm = File.ReadAllText(Path.Combine(host, "Components", "ViewModels", "Notifications", "NotificationsViewModel.cs"));
        Assert.Contains("Vm.NeedsInputCount", bell);
        Assert.Contains("Open a request to continue the paused run.", bell);
        Assert.Contains("NeedsInputCount", vm);
        Assert.Contains("Permission.ChainsManage", vm);
        Assert.Contains("Permission.JobsRun", vm);
        Assert.DoesNotContain("/project/{ProjectId}/monitor", bell);
        Assert.DoesNotContain("Agent Store", bell);
    }

    private static string FindHostRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var host = Path.Combine(dir.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
            {
                return host;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PlaceContext.Host root not found.");
    }
}
