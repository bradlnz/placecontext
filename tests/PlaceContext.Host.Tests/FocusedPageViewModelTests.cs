using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class FocusedPageViewModelTests
{
    [Theory]
    [InlineData("Overview", "OverviewViewModel")]
    [InlineData("DataEntities", "DataEntitiesViewModel")]
    [InlineData("Events", "EventsViewModel")]
    [InlineData("JobTests", "JobTestsViewModel")]
    [InlineData("Inspector", "InspectorViewModel")]
    [InlineData("ProjectView", "ProjectViewModel")]
    public void Target_page_uses_its_focused_view_model(string pageName, string viewModelName)
    {
        var page = ReadHostSource($"Components/Pages/{pageName}.razor");

        Assert.Contains($"@inject {viewModelName} Vm", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IPlaceContextService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject PortalUiState", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject NavigationManager", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IJSRuntime", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Attach", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Detach", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_tabs_are_typed_and_unknown_tabs_fall_back_to_overview()
    {
        Assert.Equal(ProjectViewTabs.Overview, ProjectViewModel.NormalizeTab("overview"));
        Assert.Equal(
            ProjectViewTabs.Requirements,
            ProjectViewModel.NormalizeTab("requirements")
        );
        Assert.Equal(ProjectViewTabs.Activity, ProjectViewModel.NormalizeTab("activity"));
        Assert.Equal(ProjectViewTabs.Overview, ProjectViewModel.NormalizeTab("retired-tab"));
    }

    [Fact]
    public void Entity_browse_tab_and_form_presentation_are_vm_decisions()
    {
        var vm = new EntityBrowseViewModel(null!, null!);

        Assert.True(vm.IsRecordsTab);
        vm.SelectGraph();
        Assert.True(vm.IsGraphTab);
        vm.SelectRecords();
        Assert.True(vm.IsRecordsTab);
        Assert.True(vm.IsChartInput("checkbox"));
        Assert.Equal("datetime-local", vm.FormInputType("timestamptz"));
    }

    [Fact]
    public void Communication_mode_predicates_preserve_form_behavior()
    {
        var vm = new CommunicationsSettingsViewModel(null!, null!);

        vm.AuthType = CommunicationsSettingsViewModel.HeaderAuth;
        vm.Kind = CommunicationsSettingsViewModel.PostmarkKind;

        Assert.True(vm.IsHeaderAuth);
        Assert.True(vm.UsesSecret);
        Assert.True(vm.IsTransactionalKind);
        Assert.True(vm.IsPostmark);
        Assert.False(vm.IsTwilio);
        Assert.Equal("recipient@example.com", vm.TestRecipientPlaceholder("email"));
    }

    [Theory]
    [InlineData("Ok", "var(--good)", "var(--good-bg)")]
    [InlineData("Warn", "var(--warn)", "var(--warn-bg)")]
    [InlineData("Failed", "var(--bad)", "var(--bad-bg)")]
    public void Inspector_status_presentation_is_centralized(
        string status,
        string color,
        string background
    )
    {
        Assert.Equal(color, InspectorViewModel.StatusColor(status));
        Assert.Equal(background, InspectorViewModel.StatusBackground(status));
    }

    [Theory]
    [InlineData(0, "0 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1.0 s")]
    public void Job_test_duration_formatting_is_a_view_model_decision(
        long milliseconds,
        string expected
    )
    {
        Assert.Equal(expected, JobTestsViewModel.FormatDuration(milliseconds));
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
