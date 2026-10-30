using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class SmartDashboardsViewModelTests
{
    [Fact]
    public async Task Measures_are_available_without_model_calls_and_decisions_require_explicit_evaluation()
    {
        var service = Fixture();
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid());
        var signal = vm.Signals.Single();
        Assert.Equal(3, signal.Measurement!.Count);
        Assert.Equal(60, signal.Measurement.Maximum);
        Assert.Equal(20, signal.Measurement.Median);
        Assert.Equal(30, signal.Measurement.Mean);
        Assert.Equal(50, signal.Measurement.Range);
        Assert.Equal(0, service.DecisionCalls);
        Assert.Equal(0, service.Analyses);
        Assert.Equal("Ready for Jev", vm.DecisionLabel(signal));

        vm.ShowDecisions();
        Assert.Equal(0, service.DecisionCalls);
        await vm.EvaluateDecisionsAsync();
        Assert.Equal(1, service.DecisionCalls);
        Assert.Equal([signal.Id], service.LastSignalIds);
        Assert.Equal(DashboardDecisionStatus.Review, vm.DecisionFor(signal.Id)!.Status);
        Assert.Equal(1, vm.ReviewDecisionCount);
        Assert.Equal(DashboardLens.Decisions, vm.Lens);
        Assert.Equal(DashboardConfidence.Medium, vm.Profile.Confidence);
    }

    [Fact]
    public async Task Targets_save_with_the_view_and_changes_invalidate_decisions_without_calling_Jev()
    {
        var service = Fixture();
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid());
        await vm.EvaluateDecisionsAsync();
        var id = vm.FocusedSignal!.Id;
        vm.EditTarget();
        vm.TargetStatistic = DashboardMeasureStatistic.Maximum;
        vm.TargetDirection = DashboardTargetDirection.AtMost;
        vm.TargetThreshold = "50";
        vm.ApplyTarget();
        Assert.True(vm.TargetsDirty);
        Assert.True(vm.FocusedSignal!.TargetBreached);
        Assert.Null(vm.DecisionFor(id));
        Assert.Equal(1, service.DecisionCalls);

        await vm.SaveAsync();
        Assert.False(vm.TargetsDirty);
        Assert.Equal(50, Assert.Single(service.Saved.Single().Targets!).Threshold);
        vm.PreviewAvailable();
        Assert.Empty(vm.Targets);
        vm.Open(service.Saved.Single());
        Assert.Equal(50, Assert.Single(vm.Targets).Threshold);
        await vm.EvaluateDecisionsAsync();
        Assert.NotNull(vm.DecisionFor(id));
        vm.EditTarget(); vm.TargetThreshold = "70"; vm.ApplyTarget();
        Assert.False(vm.FocusedSignal!.TargetBreached);
        Assert.Null(vm.DecisionFor(id));
        Assert.Equal(2, service.DecisionCalls);
    }

    [Fact]
    public async Task Browsing_preserves_current_decisions_but_refresh_and_missing_evidence_clear_them()
    {
        var service = Fixture();
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid()); await vm.SaveAsync();
        await vm.EvaluateDecisionsAsync();
        var id = vm.Signals.Single().Id;
        vm.SetLens(DashboardLens.Overview); vm.ProfileSearch = "queue";
        Assert.NotNull(vm.DecisionFor(id));
        Assert.Equal(1, service.DecisionCalls);
        await vm.RefreshAsync();
        Assert.Null(vm.DecisionFor(id));
        await vm.EvaluateDecisionsAsync();
        service.Charts = [];
        await vm.RefreshAsync();
        Assert.True(vm.MissingCharts);
        Assert.False(vm.CanEvaluateDecisions);
        Assert.Null(vm.DecisionFor(id));
        await vm.EvaluateDecisionsAsync();
        Assert.Equal(2, service.DecisionCalls);
    }

    [Fact]
    public async Task Stale_signals_require_data_and_invalid_target_input_does_not_apply_a_policy()
    {
        var service = Fixture();
        service.Charts = [service.Charts[0] with { GeneratedAt = DateTimeOffset.UtcNow.AddDays(-10) }];
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid());
        Assert.True(vm.Signals.Single().NeedsData);
        Assert.Equal("Needs data", vm.DecisionLabel(vm.Signals.Single()));
        await vm.EvaluateDecisionsAsync();
        Assert.Equal(0, service.DecisionCalls);
        vm.EditTarget(); vm.TargetThreshold = "NaN"; vm.ApplyTarget();
        Assert.Empty(vm.Targets);
        Assert.NotNull(vm.TargetError);
        Assert.True(vm.TargetEditorOpen);
    }

    [Fact]
    public async Task A_response_for_other_evidence_is_rejected_and_does_not_preserve_an_old_recommendation()
    {
        var service = Fixture();
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid()); await vm.EvaluateDecisionsAsync();
        var id = vm.Signals.Single().Id;
        Assert.NotNull(vm.DecisionFor(id));
        service.WrongDecisionFingerprint = true;
        await vm.EvaluateSignalAsync(id);
        Assert.Null(vm.DecisionFor(id));
        Assert.NotNull(vm.Error);
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Running_review_can_be_canceled_and_duplicate_clicks_do_not_start_another_batch()
    {
        var service = Fixture(); service.DelayDecisions = true;
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid());
        var review = vm.EvaluateDecisionsAsync();
        await service.DecisionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.EvaluatingDecisions);
        Assert.True(vm.Busy);
        await vm.EvaluateDecisionsAsync();
        Assert.Equal(1, service.DecisionCalls);
        vm.CancelDecisionReview(); await review;
        Assert.False(vm.Busy);
        Assert.False(vm.EvaluatingDecisions);
        Assert.Null(vm.Error);
        Assert.Equal(0, vm.EvaluatedDecisionCount);
        Assert.Contains("canceled", vm.Notice);
    }

    [Fact]
    public async Task Decision_queue_does_not_render_unmounted_chart_canvases()
    {
        var service = Fixture(); var js = new DashboardTestJs();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), js);
        await vm.LoadAsync(Guid.NewGuid()); await vm.RenderAsync();
        Assert.Equal(1, js.Renders);
        vm.ShowDecisions(); await vm.RenderAsync();
        Assert.Empty(vm.DisplayedCharts); Assert.Equal(1, js.Renders);
        vm.SetLens(DashboardLens.Overview); await vm.RenderAsync();
        Assert.Equal(2, js.Renders);
    }

    [Fact]
    public async Task A_late_success_after_cancellation_is_discarded_even_when_the_service_ignores_the_token()
    {
        var service = Fixture(); service.DelayDecisions = true; service.IgnoreDecisionCancellation = true;
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid());
        var review = vm.EvaluateDecisionsAsync();
        await service.DecisionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.CancelDecisionReview(); service.ReleaseDecision.TrySetResult(); await review;
        Assert.Equal(0, vm.EvaluatedDecisionCount);
        Assert.Null(vm.Error);
        Assert.Contains("canceled", vm.Notice);
    }

    [Fact]
    public async Task Server_freshness_gate_overrides_an_older_client_measurement_without_a_second_model_attempt()
    {
        var service = Fixture(); service.ServerRequiresData = true;
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid());
        Assert.True(vm.CanEvaluateDecisions);
        await vm.EvaluateDecisionsAsync();
        var signal = vm.Signals.Single();
        Assert.True(vm.RequiresData(signal));
        Assert.Equal("Needs data", vm.DecisionLabel(signal));
        Assert.False(vm.CanEvaluateDecisions);
        await vm.EvaluateSignalAsync(signal.Id);
        Assert.Equal(1, service.DecisionCalls);
    }

    [Fact]
    public async Task Source_editor_requires_finishing_the_selection_before_evaluating()
    {
        var service = Fixture();
        using var vm = Create(service);
        await vm.LoadAsync(Guid.NewGuid()); vm.Edit();
        Assert.False(vm.CanEvaluateDecisions);
        await vm.EvaluateDecisionsAsync();
        await vm.EvaluateSignalAsync(vm.Signals.Single().Id);
        Assert.Equal(0, service.DecisionCalls);
        vm.Cancel(); Assert.True(vm.CanEvaluateDecisions);
    }

    private static PersonalDashboardsViewModel Create(DashboardUiStub service)
        => new(service, new PortalUiState(), new DashboardTestJs());
    private static DashboardUiStub Fixture() => new()
    {
        Charts = [new(Guid.NewGuid(), "Queue demand", new ChartSpec("bar", "Queue demand", ["A", "B", "C"],
            [new ChartSeries("Calls", [10, 20, 60])]), DateTimeOffset.UtcNow, "queue_metrics")]
    };
}
