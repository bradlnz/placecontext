using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Tests;

public sealed class PersonalDashboardsViewModelTests
{
    [Fact]
    public async Task Custom_layout_survives_save_and_analysis_is_invalidated_by_changes()
    {
        var service = new DashboardUiStub(); var js = new DashboardTestJs();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), js);
        await vm.LoadAsync(Guid.NewGuid()); vm.New(); vm.Name = "My sales";
        foreach (var chart in service.Charts) vm.Toggle(chart.Id);
        vm.Move(service.Charts[1].Id, -1); vm.Question = "Is A greater than zero?";
        await vm.SaveAsync(); Assert.False(vm.Editing); Assert.Equal("My sales", vm.Selected!.Name);
        Assert.Equal(service.Charts[1].Id, vm.Selected.ChartIds[0]);
        await vm.RenderAsync(); await vm.RenderAsync(); Assert.Equal(1, js.Renders);
        await vm.AnalyzeAsync(); Assert.True(vm.AnalysisCurrent);
        vm.Question = "A different question?"; Assert.False(vm.AnalysisCurrent);
        await vm.AnalyzeAsync(); Assert.True(vm.AnalysisCurrent);
        await vm.RefreshAsync(); Assert.Null(vm.Analysis);
        await vm.RenderAsync(); Assert.Equal(2, js.Renders);
        await vm.DeleteAsync(); Assert.Single(service.Saved);
        vm.ConfirmDelete = true; await vm.DeleteAsync(); Assert.Empty(service.Saved);
    }

    [Fact]
    public async Task Existing_charts_open_as_an_unsaved_profile_preview_and_suggestions_do_not_call_a_model()
    {
        var service = new DashboardUiStub();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), new DashboardTestJs());
        await vm.LoadAsync(Guid.NewGuid());

        Assert.True(vm.IsPreview);
        Assert.False(vm.Editing);
        Assert.Equal(service.Charts.Count, vm.Profile.Charts.Count);
        Assert.Empty(service.Saved);
        Assert.Equal(0, service.Analyses);
        var confidence = vm.Profile.Confidence;
        var suggestion = Assert.Single(vm.FocusedProfile!.SuggestedQuestions);
        vm.UseSuggestion(suggestion);
        Assert.Equal(suggestion, vm.Question);
        Assert.Null(vm.Analysis);
        Assert.Equal(0, service.Analyses);
        await vm.AnalyzeAsync();
        Assert.True(vm.AnalysisCurrent);
        Assert.Equal(1, service.Analyses);
        Assert.Equal(confidence, vm.Profile.Confidence);

        var other = service.Charts.First(x => x.Id != vm.FocusedChartId);
        vm.Focus(other.Id);
        Assert.Null(vm.Analysis);
        vm.UseSuggestion("An unrelated question supplied by a stale view");
        Assert.Equal(suggestion, vm.Question);
    }

    [Fact]
    public async Task Lenses_and_search_focus_the_relevant_source_without_altering_saved_data()
    {
        var service = new DashboardUiStub();
        service.Charts = [service.Charts[0] with { GeneratedAt = DateTimeOffset.UtcNow.AddDays(-10), Source = "historic_queues" },
            service.Charts[1] with { Source = "live_calls" }];
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), new DashboardTestJs());
        await vm.LoadAsync(Guid.NewGuid());

        Assert.Equal(service.Charts[0].Id, vm.FocusedChartId);
        Assert.Equal(DashboardConfidence.Low, vm.Profile.Confidence);
        vm.SetLens(DashboardLens.NeedsAttention);
        Assert.Equal(service.Charts[0].Id, Assert.Single(vm.VisibleProfiles).ChartId);
        vm.SetLens(DashboardLens.Explore);
        Assert.Equal(2, vm.VisibleProfiles.Count);
        vm.ProfileSearch = "live_calls";
        Assert.Equal(service.Charts[1].Id, Assert.Single(vm.VisibleProfiles).ChartId);
        Assert.Equal(service.Charts[1].Id, vm.FocusedChartId);
        vm.ProfileSearch = "not a source";
        Assert.Empty(vm.VisibleProfiles);
        Assert.Null(vm.FocusedChartId);
        vm.ProfileSearch = "";
        Assert.NotNull(vm.FocusedChartId);
        Assert.Equal(2, vm.ChartIds.Count);
        Assert.Empty(service.Saved);
        Assert.Equal(0, service.Analyses);
    }

    [Fact]
    public async Task Preview_is_bounded_and_saved_order_is_preserved_by_the_explore_lens()
    {
        var service = new DashboardUiStub();
        service.Charts = Enumerable.Range(1, 15).Select(i => new DashboardChart(Guid.NewGuid(), $"Source {i}",
            new ChartSpec("bar", $"Source {i}", ["A"], [new ChartSeries("Count", [i])]), DateTimeOffset.UtcNow.AddHours(-i))).ToArray();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), new DashboardTestJs());
        await vm.LoadAsync(Guid.NewGuid());
        Assert.True(vm.IsPreview);
        Assert.Equal(12, vm.ChartIds.Count);
        Assert.Equal(15, vm.Charts.Count);
        vm.Edit();
        vm.Move(vm.ChartIds[1], -1);
        var chosenOrder = vm.ChartIds.ToArray();
        await vm.SaveAsync();
        Assert.Equal(chosenOrder, vm.Selected!.ChartIds);
        vm.SetLens(DashboardLens.Explore);
        Assert.Equal(chosenOrder, vm.VisibleProfiles.Select(x => x.ChartId).ToArray());
        vm.SetLens(DashboardLens.Overview);
        Assert.Equal(chosenOrder, vm.ChartIds);
        Assert.Equal(chosenOrder, vm.Selected.ChartIds);
    }

    [Fact]
    public async Task Missing_source_lowers_combined_confidence_and_blocks_analysis_until_selection_is_fixed()
    {
        var service = new DashboardUiStub();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), new DashboardTestJs());
        await vm.LoadAsync(Guid.NewGuid());
        await vm.SaveAsync();
        service.Charts = [service.Charts[0]];
        await vm.RefreshAsync();

        Assert.True(vm.MissingCharts);
        Assert.Equal(DashboardConfidence.Unknown, vm.Profile.Confidence);
        Assert.Equal(1, vm.Profile.NeedsAttentionCount);
        vm.Question = "Are all values positive?";
        Assert.False(vm.CanAnalyze);
        await vm.AnalyzeAsync();
        Assert.Equal(0, service.Analyses);
        vm.Edit(); vm.RemoveMissing(); await vm.SaveAsync();
        Assert.False(vm.MissingCharts);
        Assert.Single(vm.Selected!.ChartIds);
        Assert.Equal(DashboardConfidence.Medium, vm.Profile.Confidence);
    }

    [Fact]
    public async Task Reloaded_overview_tracks_new_and_removed_charts_without_resetting_investigation_context()
    {
        var service = new DashboardUiStub();
        var originalCharts = service.Charts;
        service.Charts = [];
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), new DashboardTestJs());
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Empty(vm.ChartIds);
        vm.Question = "Are displayed values positive?";
        vm.SetLens(DashboardLens.Explore);
        vm.ProfileSearch = "Chart";

        service.Charts = originalCharts;
        await vm.RefreshAsync();
        Assert.True(vm.IsPreview);
        Assert.Equal(originalCharts.Select(x => x.Id), vm.ChartIds);
        Assert.Equal(2, vm.Profile.Charts.Count);

        var added = originalCharts[0] with { Id = Guid.NewGuid(), Name = "Chart 3" };
        service.Charts = [originalCharts[1], added];
        await vm.RefreshAsync();
        Assert.Equal(service.Charts.Select(x => x.Id), vm.ChartIds);
        Assert.False(vm.MissingCharts);
        Assert.DoesNotContain(originalCharts[0].Id, vm.ChartIds);
        Assert.Equal("Are displayed values positive?", vm.Question);
        Assert.Equal(DashboardLens.Explore, vm.Lens);
        Assert.Equal("Chart", vm.ProfileSearch);
        Assert.Empty(service.Saved);
        Assert.Equal(0, service.Analyses);
    }

    [Fact]
    public async Task Reload_does_not_replace_an_unsaved_selection_while_it_is_being_edited()
    {
        var service = new DashboardUiStub();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), new DashboardTestJs());
        await vm.LoadAsync(Guid.NewGuid());
        vm.Edit(); vm.Toggle(service.Charts[0].Id);
        var selection = vm.ChartIds.ToArray();
        service.Charts = [.. service.Charts, service.Charts[0] with { Id = Guid.NewGuid() }];
        await vm.RefreshAsync();
        Assert.True(vm.Editing);
        Assert.Equal(selection, vm.ChartIds);
    }

    [Fact]
    public async Task A_failed_project_change_does_not_display_the_previous_projects_profiles()
    {
        var service = new DashboardUiStub();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), new DashboardTestJs());
        await vm.LoadAsync(Guid.NewGuid()); vm.Question = "Are all values positive?"; await vm.AnalyzeAsync();
        Assert.True(vm.AnalysisCurrent);
        service.FailRead = true;
        await vm.LoadAsync(Guid.NewGuid());
        Assert.Empty(vm.Charts);
        Assert.Empty(vm.ChartIds);
        Assert.Empty(vm.Profile.Charts);
        Assert.Equal(DashboardConfidence.Unknown, vm.Profile.Confidence);
        Assert.Null(vm.Analysis);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public void Accessible_values_do_not_index_past_ragged_series_or_present_nonfinite_numbers()
    {
        var series = new ChartSeries("Calls", [1, double.NaN]);
        Assert.Equal("Not available", PersonalDashboardsViewModel.ValueAt(series, -1));
        Assert.Equal("Not available", PersonalDashboardsViewModel.ValueAt(series, 1));
        Assert.Equal("Not available", PersonalDashboardsViewModel.ValueAt(series, 2));
        Assert.Equal("Not available", PersonalDashboardsViewModel.ValueAt(null, 0));
    }
}
