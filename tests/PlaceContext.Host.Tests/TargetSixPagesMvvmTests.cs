using System.Reflection;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class TargetSixPagesMvvmArchitectureTests
{
    private static readonly string[] Pages =
    [
        "Chat.razor",
        "DataMap.razor",
        "EntityBrowse.razor",
        "JobChains.razor",
        "Jobs.razor",
        "ProjectData.razor",
    ];

    [Fact]
    public void Already_backed_pages_have_only_their_view_model_injected()
    {
        foreach (var pageName in Pages)
        {
            var page = ReadHostSource($"Components/Pages/{pageName}");
            var injections = page.Split('\n')
                .Where(line => line.TrimStart().StartsWith("@inject", StringComparison.Ordinal))
                .Select(line => line.Trim())
                .ToArray();

            Assert.Single(injections);
            Assert.Contains("ViewModel Vm", injections[0], StringComparison.Ordinal);
            Assert.DoesNotContain("PortalUiState", page, StringComparison.Ordinal);
            Assert.DoesNotContain("NavigationManager", page, StringComparison.Ordinal);
            Assert.DoesNotContain("IJSRuntime", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void View_models_own_the_remaining_page_decisions()
    {
        AssertHasPublicMember<DataMapViewModel>("OrderedJobs");
        AssertHasPublicMember<DataMapViewModel>("MappedJobCount");
        AssertHasPublicMember<EntityBrowseViewModel>("EntityNameForTable");
        AssertHasPublicMember<JobChainsViewModel>("JobsRoute");
        AssertHasPublicMember<JobsViewModel>("AutomationPercent");
        AssertHasPublicMember<ProjectDataViewModel>("IsJsonCell");
    }

    private static void AssertHasPublicMember<T>(string name)
    {
        Assert.NotNull(
            typeof(T)
                .GetMember(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault()
        );
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
                return File.ReadAllText(Path.Combine(host, relativePath));
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
