using PlaceContext.Application.Build;

namespace PlaceContext.Host.Tests;

public sealed class DataWorkflowSaasFoundationTests
{
    [Fact]
    public void Workflow_editor_exposes_data_and_execution_readiness_without_a_second_product_section()
    {
        var root = HostRoot();
        var razor = File.ReadAllText(Path.Combine(root, "Components/Pages/JobChains.razor"));
        var editor = File.ReadAllText(Path.Combine(root, "Components/ViewModels/JobChains/JobChainsViewModel.Editor.cs"));
        Assert.Contains("Definition checks", razor);
        Assert.Contains("Vm.CheckReadiness", razor);
        Assert.Contains("WorkflowReadiness.Evaluate", editor);
        Assert.Contains("expectedUpdatedAt: _openedWorkflowUpdatedAt", editor);
        Assert.DoesNotContain("/monitor", razor);
    }

    [Fact]
    public void Build_exposes_read_only_workflow_readiness()
    {
        var tools = new BuildTools(Guid.NewGuid(), null!, null!, null!);
        var names = tools.Functions().Select(x => x.Name).ToArray();
        Assert.Contains("check_workflow_readiness", names);
        Assert.Contains("save_workflow", names);
    }

    private static string HostRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
