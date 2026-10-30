using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;

namespace PlaceContext.Application.Tests.Dashboards;

public sealed class DashboardSignalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
    private static DashboardChart Chart() => new(Guid.NewGuid(), "Voice quality", new ChartSpec("bar", "Voice quality",
        ["Brisbane", "Sydney", "Melbourne", "Perth", "Auckland"], [new ChartSeries("Affected calls", [18, 34, 22, 10, 16])]), Now, "call_quality");

    [Fact]
    public void Measures_exact_displayed_distribution_without_inventing_units_or_time_order()
    {
        var chart = Chart();
        var signal = Assert.Single(DashboardSignals.Create(new([chart]), Now));
        var measure = Assert.IsType<DashboardMeasurement>(signal.Measurement);
        Assert.Equal(5, measure.Count);
        Assert.Equal(10, measure.Minimum);
        Assert.Equal(34, measure.Maximum);
        Assert.Equal(20, measure.Mean);
        Assert.Equal(18, measure.Median);
        Assert.Equal(24, measure.Range);
        Assert.Equal("Perth", measure.MinimumLabel);
        Assert.Equal("Sydney", measure.MaximumLabel);
        Assert.Equal($"chart:{chart.Id:N}", signal.Id);
        Assert.Equal(DashboardConfidence.Medium, signal.DataConfidence);
        Assert.False(signal.NeedsData);
        Assert.Null(signal.Target);
        Assert.Contains("No watch target", signal.Rationale);
        Assert.Contains("Do not infer units, chronological order", signal.Question);
    }

    [Fact]
    public void Measurements_follow_display_limits_and_reject_undefined_arithmetic()
    {
        var labels = Enumerable.Range(0, 40).Select(i => i.ToString()).ToArray();
        var measure = DashboardMeasurement.Create(new("Count", Enumerable.Range(0, 40).Select(i => (double)i).ToArray()), labels)!;
        Assert.Equal(ChartSpec.MaxLabels, measure.Count);
        Assert.Equal(23, measure.Maximum);
        Assert.Equal(11.5, measure.Mean);
        Assert.Equal(11.5, measure.Median);
        Assert.Null(DashboardMeasurement.Create(new("Count", [double.NaN, 1]), ["A", "B"]));
        Assert.Null(DashboardMeasurement.Create(new("Count", [1]), ["A", "B"]));
        Assert.Null(DashboardMeasurement.Create(new("Count", [-double.MaxValue, double.MaxValue]), ["A", "B"]));
        var large = DashboardMeasurement.Create(new("Count", [double.MaxValue, double.MaxValue]), ["A", "B"]);
        Assert.Equal(double.MaxValue, large!.Mean);
        Assert.Equal(double.MaxValue, large.Median);
        var compensated = DashboardMeasurement.Create(new("Count", [1e16, 1, -1e16]), ["A", "B", "C"]);
        Assert.Equal(1d / 3, compensated!.Mean);
    }

    [Fact]
    public void Target_selects_its_named_measure_and_equality_meets_raw_value_targets()
    {
        var chart = Chart();
        chart = chart with { Spec = chart.Spec with { Series = [.. chart.Spec.Series, new("Unassigned", [2, 8, 4, 6, 0])] } };
        var target = new DashboardWatchTarget(chart.Id, "Unassigned", DashboardMeasureStatistic.Maximum, DashboardTargetDirection.AtMost, 8);
        var signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target]));
        Assert.Equal("Unassigned", signal.SeriesName);
        Assert.Equal(8, signal.ObservedTargetValue);
        Assert.False(signal.TargetBreached);
        Assert.Equal(0, signal.TargetTolerance);
        signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target with { Threshold = Math.BitDecrement(8) }]));
        Assert.True(signal.TargetBreached);
        signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target with { Statistic = DashboardMeasureStatistic.Minimum, Direction = DashboardTargetDirection.AtLeast, Threshold = 1 }]));
        Assert.True(signal.TargetBreached);
    }

    [Theory]
    [InlineData(DashboardMeasureStatistic.Mean)]
    [InlineData(DashboardMeasureStatistic.Median)]
    public void Computed_decimal_equality_allows_only_arithmetic_roundoff(DashboardMeasureStatistic statistic)
    {
        var chart = Chart() with { Spec = new("bar", "Decimal values", ["A", "B"], [new("Measure", [.1, .2])]) };
        var target = new DashboardWatchTarget(chart.Id, "Measure", statistic, DashboardTargetDirection.AtMost, .15);
        var signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target]));
        Assert.False(signal.TargetBreached);
        Assert.InRange(signal.TargetTolerance, double.Epsilon, 1e-15);
        signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target with { Direction = DashboardTargetDirection.AtLeast }]));
        Assert.False(signal.TargetBreached);
        signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target with { Threshold = .1499999999 }]));
        Assert.True(signal.TargetBreached);
    }

    [Fact]
    public void Queue_mean_at_a_configured_equal_target_is_not_a_breach()
    {
        double[] values = [8, 14, 12, 27, 35, 29, 19, 16, 22, 10];
        var chart = Chart() with { Spec = new("line", "Queue", Enumerable.Range(0, values.Length).Select(i => i.ToString()).ToArray(), [new("Calls", values)]) };
        var target = new DashboardWatchTarget(chart.Id, "Calls", DashboardMeasureStatistic.Mean, DashboardTargetDirection.AtMost, 19.2);
        var signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target]));
        Assert.Equal(19.2, signal.Measurement!.Mean);
        Assert.Equal(17.5, signal.Measurement.Median);
        Assert.False(signal.TargetBreached);
    }

    [Fact]
    public void Stale_ambiguous_or_incompatible_evidence_cannot_receive_a_model_recommendation()
    {
        var chart = Chart();
        var stale = Assert.Single(DashboardSignals.Create(new([chart]), Now.AddDays(8)));
        Assert.True(stale.NeedsData);
        Assert.NotNull(stale.Measurement);
        Assert.Equal(DashboardDecisionStatus.NeedsData, DashboardDecision.FromJudgment(stale, 1, "test", Now, "evidence").Status);
        Assert.Null(DashboardDecision.FromJudgment(stale, 1, "test", Now, "evidence").Probability);
        var duplicate = chart with { Spec = chart.Spec with { Labels = ["A", "A", "B", "C", "D"] } };
        Assert.True(Assert.Single(DashboardSignals.Create(new([duplicate]), Now)).NeedsData);
        var missing = new DashboardWatchTarget(chart.Id, "Missing measure", DashboardMeasureStatistic.Maximum, DashboardTargetDirection.AtMost, 10);
        var signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [missing]));
        Assert.True(signal.NeedsData);
        Assert.Equal("Missing measure", signal.SeriesName);
        Assert.Null(signal.Measurement);
        Assert.Contains("missing or ambiguous", signal.Rationale);
        Assert.Throws<InvalidOperationException>(() => DashboardSignals.ValidateTargets(new([chart]), [missing]));
        var badEnum = missing with { SeriesName = "Affected calls", Statistic = (DashboardMeasureStatistic)99 };
        Assert.True(Assert.Single(DashboardSignals.Create(new([chart]), Now, [badEnum])).NeedsData);
    }

    [Theory]
    [InlineData(1, DashboardDecisionStatus.Review)]
    [InlineData(.9, DashboardDecisionStatus.Review)]
    [InlineData(.5, DashboardDecisionStatus.Hold)]
    [InlineData(.1, DashboardDecisionStatus.Watch)]
    [InlineData(0, DashboardDecisionStatus.Watch)]
    [InlineData(double.NaN, DashboardDecisionStatus.Unavailable)]
    [InlineData(1.01, DashboardDecisionStatus.Unavailable)]
    public void Jev_gates_the_small_decision_without_promoting_data_confidence(double score, DashboardDecisionStatus status)
    {
        var signal = Assert.Single(DashboardSignals.Create(new([Chart()]), Now));
        var decision = DashboardDecision.FromJudgment(signal, score, "jev-test", Now, "fingerprint");
        Assert.Equal(status, decision.Status);
        Assert.Equal("fingerprint", decision.Fingerprint);
        Assert.Equal(signal.Question, decision.Question);
        Assert.Equal(DashboardConfidence.Medium, signal.DataConfidence);
        if (status == DashboardDecisionStatus.Unavailable) Assert.Null(decision.Probability);
        else Assert.Equal(score, decision.Probability);
    }

    [Theory]
    [InlineData(30, .05)]
    [InlineData(40, .95)]
    public void Judgment_conflicting_with_an_explicit_measured_target_is_held(double threshold, double score)
    {
        var chart = Chart();
        var target = new DashboardWatchTarget(chart.Id, "Affected calls", DashboardMeasureStatistic.Maximum, DashboardTargetDirection.AtMost, threshold);
        var signal = Assert.Single(DashboardSignals.Create(new([chart]), Now, [target]));
        var decision = DashboardDecision.FromJudgment(signal, score, "jev-test", Now, "fingerprint");
        Assert.Equal(DashboardDecisionStatus.Hold, decision.Status);
        Assert.Contains("conflicts", decision.Rationale);
        Assert.Equal(score, decision.Probability);
    }

    [Fact]
    public void Evidence_and_policy_changes_invalidate_decisions_but_policy_order_and_age_do_not_change_content_identity()
    {
        var first = Chart(); var second = Chart();
        var snapshot = new DashboardSnapshot([first, second]);
        var target = new DashboardWatchTarget(first.Id, "Affected calls", DashboardMeasureStatistic.Maximum, DashboardTargetDirection.AtMost, 40);
        var other = target with { ChartId = second.Id };
        var fingerprint = DashboardSignals.Fingerprint(snapshot, [target, other]);
        Assert.Equal(fingerprint, DashboardSignals.Fingerprint(snapshot, [other, target]));
        Assert.NotEqual(fingerprint, DashboardSignals.Fingerprint(snapshot, [target with { Threshold = 30 }, other]));
        Assert.NotEqual(fingerprint, DashboardSignals.Fingerprint(new([first with { Source = "different" }, second]), [target, other]));
        Assert.False(DashboardSignals.Create(snapshot, Now, [target, other])[0].NeedsData);
        Assert.True(DashboardSignals.Create(snapshot, Now.AddDays(8), [target, other])[0].NeedsData);
        Assert.Equal(fingerprint, DashboardSignals.Fingerprint(snapshot, [target, other]));
        var signal = DashboardSignals.Create(snapshot, Now, [target, other])[0];
        var evidence = DashboardSignals.Evidence(first, signal);
        Assert.Contains(first.Id.ToString(), evidence);
        Assert.DoesNotContain(second.Id.ToString(), evidence);
        Assert.Contains("AtMost", evidence);
        Assert.Contains("Maximum", evidence);
        Assert.Contains("Source coverage", DashboardDataProfile.Create(first, Now).Checks.Single(x => x.Key == "coverage").Name);
    }
}
