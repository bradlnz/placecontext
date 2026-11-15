using PlaceContext.Application.Build;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tests;

public sealed class WorkflowFirstNavigationTests
{
    [Fact]
    public void Default_menu_prioritises_workflows_and_omits_retired_destinations()
    {
        var layout = new MenuConfigService(null!, null!, null!, null!).DefaultLayout();
        var ids = layout.Workspace.Select(item => item.Id).ToArray();
        Assert.Contains("build.chat", ids);
        Assert.Contains("chains", ids);
        Assert.DoesNotContain("build.library", ids);
        Assert.DoesNotContain("dashboards", ids);
        Assert.DoesNotContain("monitor", ids);
        Assert.True(Array.IndexOf(ids, "chains") < Array.IndexOf(ids, "jobs"));
        Assert.Null(Assert.Single(layout.Workspace, item => item.Id == "chains").Section);
        Assert.DoesNotContain(layout.Workspace, item => item.Section == "Automation");
    }

    [Fact]
    public void Monitoring_and_human_approvals_remain_within_workflows()
    {
        var root = HostRoot();
        var page = File.ReadAllText(Path.Combine(root, "Components", "Pages", "JobChains.razor"));
        Assert.Contains("RunsAwaitingInput", page);
        Assert.Contains("RunsFailedOrPartial", page);
        Assert.Contains("RequestHumanAnswer(true)", page);
        Assert.Contains("ConfirmHumanAnswerAsync", page);
        Assert.DoesNotContain("/monitor", page);
        Assert.False(File.Exists(Path.Combine(root, "Components", "Pages", "WorkflowMonitor.razor")));
    }

    [Fact]
    public void Build_tools_expose_executable_workflows_not_catalog_agent_runs()
    {
        var tools = new BuildTools(Guid.NewGuid(), null!, null!, null!);
        var names = tools.Functions().Select(f => f.Name).ToHashSet();
        Assert.Contains("list_workflows", names);
        Assert.Contains("save_workflow", names);
        Assert.Contains("run_workflow", names);
        Assert.Contains("review_workflow_result", names);
        Assert.DoesNotContain("list_agent_workflows", names);
        Assert.DoesNotContain("run_agent_workflow", names);
    }

    [Fact]
    public void Retired_routes_have_workflow_focused_redirects_and_no_form_controls()
    {
        var root = HostRoot();
        var store = File.ReadAllText(Path.Combine(root, "Components", "Pages", "AgentWorkflows.razor"));
        var profiles = File.ReadAllText(Path.Combine(root, "Components", "Pages", "PersonalDashboards.razor"));
        Assert.Contains("/build/workflows", store);
        Assert.Contains("NavigateTo", store);
        Assert.Contains("/workflows", store);
        Assert.DoesNotContain("@inject AgentWorkflowsViewModel", store);
        Assert.Contains("/dashboards", profiles);
        Assert.Contains("NavigateTo", profiles);
        Assert.DoesNotContain("@inject PersonalDashboardsViewModel", profiles);
    }

    private static string HostRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            var path = Path.Combine(folder.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(path))
            {
                return path;
            }
            folder = folder.Parent;
        }
        throw new DirectoryNotFoundException("PlaceContext.Host root not found.");
    }
}
