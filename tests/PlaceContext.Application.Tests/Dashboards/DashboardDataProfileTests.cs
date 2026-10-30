using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;

namespace PlaceContext.Application.Tests.Dashboards;

public sealed class DashboardDataProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static DashboardChart Chart(DateTimeOffset? generatedAt = null) => new(Guid.NewGuid(), "Queue demand",
        new ChartSpec("bar", "Queue demand", ["Sales", "Support", "After hours"],
            [new ChartSeries("Calls", [120, 40, 0])]), generatedAt ?? Now, "call_queues");

    [Fact]
    public void Current_values_are_profiled_without_claiming_source_completeness()
    {
        var profile = DashboardDataProfile.Create(Chart(), Now);

        Assert.Equal(DashboardConfidence.Medium, profile.Confidence);
        Assert.Equal(DashboardFreshness.Current, profile.Freshness);
        Assert.Equal("call_queues", profile.Source);
        Assert.Equal(3, profile.LabelCount);
        Assert.Equal(3, profile.ValueCount);
        Assert.False(profile.NeedsAttention);
        Assert.True(profile.CanSummarize);
        Assert.Equal(DashboardCheckState.Caution, profile.Checks.Single(x => x.Key == "coverage").State);
        Assert.Contains("not recorded", profile.Checks.Single(x => x.Key == "coverage").Reason);
        Assert.Contains("not source record counts", profile.Checks.Single(x => x.Key == "labels").Reason);
        var series = Assert.Single(profile.Series);
        Assert.Equal(120, series.Maximum);
        Assert.Equal("Sales", series.MaximumLabel);
        Assert.Equal(0, series.Minimum);
        Assert.Equal(1, series.ZeroCount);
        Assert.Equal(DashboardCheckState.Pass, profile.Checks.Single(x => x.Key == "values").State);
        Assert.All(profile.SuggestedQuestions, question => Assert.Contains("displayed snapshot", question));
    }

    [Theory]
    [InlineData(0, DashboardFreshness.Current, DashboardConfidence.Medium)]
    [InlineData(24, DashboardFreshness.Current, DashboardConfidence.Medium)]
    [InlineData(25, DashboardFreshness.Aging, DashboardConfidence.Medium)]
    [InlineData(168, DashboardFreshness.Aging, DashboardConfidence.Medium)]
    [InlineData(169, DashboardFreshness.Stale, DashboardConfidence.Low)]
    public void Freshness_uses_documented_age_boundaries(double hours, DashboardFreshness freshness, DashboardConfidence confidence)
    {
        var profile = DashboardDataProfile.Create(Chart(Now.AddHours(-hours)), Now);
        Assert.Equal(freshness, profile.Freshness);
        Assert.Equal(confidence, profile.Confidence);
        Assert.Equal(TimeSpan.FromHours(hours), profile.Age);
    }

    [Fact]
    public void Unknown_and_future_dates_do_not_look_fresh()
    {
        var missing = DashboardDataProfile.Create(Chart(default(DateTimeOffset)), Now);
        var future = DashboardDataProfile.Create(Chart(Now.AddMinutes(6)), Now);
        foreach (var profile in new[] { missing, future })
        {
            Assert.Equal(DashboardFreshness.Unknown, profile.Freshness);
            Assert.Equal(DashboardConfidence.Low, profile.Confidence);
            Assert.Null(profile.Age);
            Assert.True(profile.NeedsAttention);
        }
        Assert.Contains("future", future.ConfidenceReason);
        var tolerated = DashboardDataProfile.Create(Chart(Now.AddMinutes(5)), Now);
        Assert.Equal(DashboardFreshness.Current, tolerated.Freshness);
        Assert.Equal(TimeSpan.Zero, tolerated.Age);
    }

    [Fact]
    public void Blank_and_repeated_categories_surface_an_actionable_issue()
    {
        var chart = Chart();
        var profile = DashboardDataProfile.Create(chart with { Spec = chart.Spec with { Labels = ["Sales", "Sales", " "] } }, Now);
        Assert.Equal(DashboardConfidence.Low, profile.Confidence);
        var check = profile.Checks.Single(x => x.Key == "labels");
        Assert.Equal(DashboardCheckState.Attention, check.State);
        Assert.Contains("1 blank", check.Reason);
        Assert.Contains("1 repeated", check.Reason);
        Assert.Contains("source query", check.NextStep);
        Assert.Empty(profile.SuggestedQuestions);
        Assert.False(profile.CanSummarize);
    }

    [Theory]
    [InlineData("Calls", "Calls", 0, 1)]
    [InlineData("Calls", " calls ", 0, 1)]
    [InlineData("Calls", " ", 1, 0)]
    public void Ambiguous_measure_names_do_not_generate_comparisons(string first, string second, int blanks, int repeated)
    {
        var chart = Chart();
        var profile = DashboardDataProfile.Create(chart with
        {
            Spec = chart.Spec with
            {
                Series = [new ChartSeries(first, [120, 40, 0]), new ChartSeries(second, [0, 40, 120])]
            }
        }, Now);
        Assert.Equal(DashboardConfidence.Low, profile.Confidence);
        Assert.True(profile.NeedsAttention);
        Assert.False(profile.CanSummarize);
        Assert.Empty(profile.SuggestedQuestions);
        var check = profile.Checks.Single(x => x.Key == "measures");
        Assert.Equal(DashboardCheckState.Attention, check.State);
        Assert.Contains($"{blanks} blank", check.Reason);
        Assert.Contains($"{repeated} repeated", check.Reason);
        Assert.Equal(2, profile.Series.Count);
        Assert.Equal("Sales", profile.Series[0].MaximumLabel);
        Assert.Equal("After hours", profile.Series[1].MaximumLabel);
    }

    [Fact]
    public void Malformed_values_are_not_silently_promoted_to_trusted_evidence()
    {
        var chart = Chart();
        var profile = DashboardDataProfile.Create(chart with
        {
            Spec = chart.Spec with { Series = [new ChartSeries("Calls", [double.NaN, 40])] }
        }, Now);
        Assert.Equal(DashboardConfidence.Low, profile.Confidence);
        Assert.Equal(1, profile.ValueCount);
        Assert.Equal(40, Assert.Single(profile.Series).Minimum);
        Assert.Equal(DashboardCheckState.Attention, profile.Checks.Single(x => x.Key == "values").State);
        Assert.Empty(profile.SuggestedQuestions);
        Assert.False(profile.CanSummarize);
    }

    [Fact]
    public void Empty_evidence_is_unknown_and_cannot_be_hidden_by_another_chart()
    {
        var empty = Chart() with { Spec = new ChartSpec("bar", "Empty", [], []) };
        var profile = DashboardDataProfile.Create(empty, Now);
        Assert.Equal(DashboardConfidence.Unknown, profile.Confidence);
        Assert.Empty(profile.Series);
        Assert.Empty(profile.SuggestedQuestions);
        var combined = DashboardSnapshotProfile.Create(new([Chart(), empty]), Now);
        Assert.Equal(DashboardConfidence.Unknown, combined.Confidence);
        Assert.Equal(1, combined.NeedsAttentionCount);
        Assert.Null(DashboardSnapshotProfile.Create(new([]), Now).OldestGeneratedAt);
    }

    [Fact]
    public void Profiling_remains_bounded_by_the_chart_display_limits()
    {
        var labels = Enumerable.Range(0, 200).Select(x => $"Queue {x}").ToArray();
        var series = Enumerable.Range(0, 10).Select(x => new ChartSeries($"Series {x}", Enumerable.Range(0, 200).Select(i => (double)i).ToArray())).ToArray();
        var chart = Chart() with { Spec = new ChartSpec("bar", "Large", labels, series) };
        var profile = DashboardDataProfile.Create(chart, Now);
        Assert.Equal(ChartSpec.MaxLabels, profile.LabelCount);
        Assert.Equal(ChartSpec.MaxSeries, profile.SeriesCount);
        Assert.Equal(ChartSpec.MaxLabels * ChartSpec.MaxSeries, profile.ValueCount);
        Assert.Contains("display limit", profile.Checks.Single(x => x.Key == "coverage").Reason);
        Assert.All(profile.Series, item => Assert.Equal(23, item.Maximum));
        Assert.InRange(profile.SuggestedQuestions.Count, 1, 3);
    }

    [Fact]
    public void Profile_age_does_not_mutate_the_evidence_fingerprint_but_source_changes_do()
    {
        var chart = Chart();
        var snapshot = new DashboardSnapshot([chart]);
        var fingerprint = snapshot.Fingerprint;
        Assert.Equal(DashboardConfidence.Medium, DashboardSnapshotProfile.Create(snapshot, Now).Confidence);
        Assert.Equal(DashboardConfidence.Low, DashboardSnapshotProfile.Create(snapshot, Now.AddDays(8)).Confidence);
        Assert.Equal(fingerprint, snapshot.Fingerprint);
        Assert.NotEqual(fingerprint, new DashboardSnapshot([chart with { Source = "different_source" }]).Fingerprint);
    }
}
