using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class DecisionTreeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Assembler_builds_root_decision_change_and_hotspot_nodes()
    {
        var pid = ProjectId.New();
        var ledger = ActivityLog.Start(pid);
        for (var i = 1; i <= 3; i++)
            ledger.Append($"change {i}", Author.Agent("claude"), Rationale.None, TestDelta.None,
                RiskDelta.None, ActivityVerification.None, new[] { "core.cs" }, Array.Empty<GraphNodeId>(), T0);

        var decisions = new[] { Decision.Record(pid, "Use EF?", "Yes", Rationale.None, T0) };
        var activity = new[] { new ToolActivity("record_activity", false), new ToolActivity("query_graph", true) };

        var tree = new DecisionTreeAssembler().Assemble(ProjectName.From("alpha"), decisions, ledger, activity);

        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.Root);
        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.Decision);
        Assert.Equal(3, tree.Nodes.Count(n => n.Kind == TreeNodeKind.Change));

        // core.cs was touched by 3 changes → a churn hotspot (god node).
        var hotspots = tree.Hotspots();
        var hot = Assert.Single(hotspots);
        Assert.Equal(3, hot.Degree);
        Assert.Equal(1, tree.ToMetrics().GodNodeCount);
    }

    [Fact]
    public void Failed_tool_call_lowers_link_confidence()
    {
        var pid = ProjectId.New();
        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            new[] { new ToolActivity("query_graph", true) });

        // The failed tool's edge is Ambiguous, so the low-confidence ratio is non-zero.
        Assert.True(tree.ToMetrics().LowConfidenceLinkRatio > 0);
    }

    [Fact]
    public void Answer_reports_hotspots()
    {
        var pid = ProjectId.New();
        var ledger = ActivityLog.Start(pid);
        for (var i = 1; i <= 4; i++)
            ledger.Append($"c{i}", Author.Agent("claude"), Rationale.None, TestDelta.None,
                RiskDelta.None, ActivityVerification.None, new[] { "hot.cs" }, Array.Empty<GraphNodeId>(), T0);

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ledger, Array.Empty<ToolActivity>());

        Assert.Contains("hot.cs", tree.Answer("what are the hotspots?"));
    }
}
