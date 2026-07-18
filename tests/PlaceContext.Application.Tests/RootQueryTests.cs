using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class RootQueryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(InMemoryProjectRepository, InMemoryActivityLogRepository, Project)> SeedAsync()
    {
        var projects = new InMemoryProjectRepository();
        var ledgers = new InMemoryActivityLogRepository();

        var p = Project.Discover(ProjectPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        p.Register(T0);
        await projects.AddAsync(p);
        return (projects, ledgers, p);
    }

    [Fact]
    public async Task RootStats_counts_today_changes_by_actor()
    {
        var (projects, ledgers, p) = await SeedAsync();
        var ledger = await ledgers.GetForProjectAsync(p.Id);
        ledger.Append("a", Author.Agent("claude"), Rationale.Of("x"), TestDelta.From(1, 0, 0),
            new ActivityVerification(true, true), new[] { "f.cs" }, Array.Empty<GraphNodeId>(), T0);
        ledger.Append("b", Author.Human("brad"), Rationale.Of("y"), TestDelta.None,
            ActivityVerification.None, new[] { "g.cs" }, Array.Empty<GraphNodeId>(), T0);
        await ledgers.SaveAsync(ledger);

        var handler = new GetRootStatsHandler(projects, ledgers, new FakeClock(T0.AddHours(3)));
        var stats = await handler.HandleAsync(new GetRootStatsQuery());

        Assert.Equal(1, stats.ProjectCount);
        Assert.Equal(2, stats.ChangesToday);
        Assert.Equal(1, stats.AgentChangesToday);
        Assert.Equal(1, stats.HumanChangesToday);
    }

    [Fact]
    public async Task RootActivity_lists_entries_with_rationale_and_files()
    {
        var (projects, ledgers, p) = await SeedAsync();
        var ledger = await ledgers.GetForProjectAsync(p.Id);
        // agent change with no rationale recorded
        ledger.Append("sloppy patch", Author.Agent("claude"), Rationale.None, TestDelta.None,
            ActivityVerification.None, new[] { "a.cs" }, Array.Empty<GraphNodeId>(), T0);
        // agent change with rationale and a test delta
        ledger.Append("clean patch", Author.Agent("claude"), Rationale.Of("why"), TestDelta.From(2, 0, 0),
            new ActivityVerification(true, true), new[] { "b.cs" }, Array.Empty<GraphNodeId>(), T0);
        await ledgers.SaveAsync(ledger);

        var handler = new GetRootActivityHandler(projects, ledgers);
        var view = await handler.HandleAsync(new GetRootActivityQuery(10));

        var sloppy = view.Entries.Single(e => e.Title == "sloppy patch");
        var clean = view.Entries.Single(e => e.Title == "clean patch");

        Assert.False(sloppy.HasRationale);
        Assert.Contains("No rationale recorded", sloppy.Why);
        Assert.True(clean.HasRationale);
        Assert.Equal("why", clean.Why);
        Assert.Equal("+2 / −0", clean.TestDelta);

        // The touched files surface on each ledger entry (not just a count).
        Assert.Equal(new[] { "a.cs" }, sloppy.Files);
        Assert.Equal(1, sloppy.FileCount);
        Assert.Equal(new[] { "b.cs" }, clean.Files);
    }
}
