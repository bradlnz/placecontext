using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;

namespace PlaceContext.Application.Tests.Dashboards;

public sealed class DashboardIndicatorProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    private static DashboardChart Chart(string name, string? source, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), name, new ChartSpec("bar", name, ["A", "B", "C"],
            [new("Contacts", [3, 6, 9]), new("Resolved", [2, 4, 8])]),
            at ?? Now, source);

    [Fact]
    public void Profiles_group_only_by_a_recorded_source_and_measure_all_named_series()
    {
        var first = Chart("Contact activity", "customers");
        var second = Chart("Contact resolution", "customers") with
        {
            Spec = new ChartSpec("line", "Contact resolution", ["A", "B", "C"],
                [new("Escalations", [0, 1, 2])])
        };
        var unrelated = Chart("Unrelated", null);
        var other = Chart("Another", null);
        var result = DashboardIndicatorProfiles.Create(new([first, second, unrelated, other]), Now);
        Assert.Equal(3, result.Count); // one recorded source and two unlinked charts
        var customers = Assert.Single(result.Where(x => x.Source == "customers"));
        Assert.Equal(3, customers.Indicators.Count);
        var contacts = Assert.Single(customers.Indicators.Where(x => x.SeriesName == "Contacts"));
        Assert.Equal(6, contacts.ObservedValue);
        Assert.Equal(3, contacts.Measurement!.Count);
        Assert.False(contacts.NeedsData);
        Assert.Equal(DashboardConfidence.Medium, customers.Confidence);
        Assert.Equal(0, customers.NeedsDataCount);
        Assert.Equal(2, result.Count(x => x.Source is null));
    }

    [Fact]
    public void A_target_changes_only_its_named_indicator_and_never_infers_other_measures()
    {
        var chart = Chart("Calls", "phone_operations");
        var target = new DashboardWatchTarget(chart.Id, "Resolved", DashboardMeasureStatistic.Maximum,
            DashboardTargetDirection.AtLeast, 8);
        var group = Assert.Single(DashboardIndicatorProfiles.Create(new([chart]), Now, [target]));
        var resolved = Assert.Single(group.Indicators.Where(x => x.SeriesName == "Resolved"));
        var contacts = Assert.Single(group.Indicators.Where(x => x.SeriesName == "Contacts"));
        Assert.Equal(8, resolved.ObservedValue);
        Assert.Equal("Maximum", resolved.ObservedStatistic);
        Assert.Equal(target, resolved.Target);
        Assert.Null(contacts.Target);
        Assert.Equal(6, contacts.ObservedValue);
        Assert.Equal("Mean", contacts.ObservedStatistic);
    }

    [Fact]
    public void Stale_and_invalid_snapshots_cannot_appear_as_trustworthy_indicators()
    {
        var stale = Chart("Historical", "customers", Now.AddDays(-8));
        var invalid = Chart("Broken", null) with
        {
            Spec = new ChartSpec("bar", "Broken", ["A", "B", "C"],
                [new("Contacts", [3, double.NaN, 9])])
        };
        var profiles = DashboardIndicatorProfiles.Create(new([stale, invalid]), Now);
        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, group => Assert.True(group.NeedsDataCount > 0));
        Assert.Contains(profiles.SelectMany(group => group.Indicators),
            indicator => indicator.Freshness == DashboardFreshness.Stale && indicator.NeedsData);
        Assert.Contains(profiles.SelectMany(group => group.Indicators),
            indicator => indicator.Measurement is null && indicator.NeedsData);
    }
}
