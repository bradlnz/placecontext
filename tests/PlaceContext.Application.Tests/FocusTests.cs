using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

public class FocusTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Focus_flags_missing_context_and_unverified_changes_high_first()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/home/brad/code/alpha"), ProjectName.From("alpha"), T0);
        project.Register(T0);
        await projects.AddAsync(project);

        var ledgers = new InMemoryActivityLogRepository();
        var ledger = ActivityLog.Start(project.Id);
        for (var i = 0; i < 3; i++)
            ledger.Append($"agent change {i}", Author.Agent("claude"), Rationale.None, TestDelta.None,
                RiskDelta.None, ActivityVerification.None, new[] { "a.cs" }, Array.Empty<GraphNodeId>(), T0);
        await ledgers.SaveAsync(ledger);

        var handler = new FocusHandler(projects, ledgers, new InMemoryProjectContextRepository(), new InMemoryRiskAssessmentRepository());
        var view = await handler.HandleAsync(new GetFocusQuery());

        var kinds = view.Items.Select(i => i.Kind).ToList();
        Assert.Contains("missing-context", kinds);
        Assert.Contains("unverified-changes", kinds);
        Assert.Contains("graphify", kinds);
        // 3 unverified agent changes → high severity, sorted to the top.
        Assert.Equal("unverified-changes", view.Items[0].Kind);
        Assert.Equal("high", view.Items[0].Severity);
        Assert.All(view.Items, i => Assert.StartsWith("/project/", i.Url));
    }

    [Fact]
    public async Task Focus_is_empty_when_nothing_needs_attention()
    {
        var projects = new InMemoryProjectRepository(); // no projects → no items
        var handler = new FocusHandler(projects, new InMemoryActivityLogRepository(),
            new InMemoryProjectContextRepository(), new InMemoryRiskAssessmentRepository());

        var view = await handler.HandleAsync(new GetFocusQuery());

        Assert.Empty(view.Items);
        Assert.Equal(0, view.ProjectCount);
    }
}
