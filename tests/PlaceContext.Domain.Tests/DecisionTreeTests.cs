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
    public void Tool_call_activity_is_excluded_from_the_dependency_graph()
    {
        var pid = ProjectId.New();
        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            new[] { new ToolActivity("query_graph", false), new ToolActivity("record_activity", true) });

        // MCP tool calls are transient access, not structural dependencies — the assembler ignores them,
        // so (with no decisions or changes) they contribute no coupling edges and the low-confidence
        // ratio stays zero rather than being inflated by tool traffic.
        Assert.Equal(0.0, tree.ToMetrics().LowConfidenceLinkRatio);
    }

    [Fact]
    public void Run_outputs_become_brain_nodes_cross_linked_by_similarity()
    {
        var pid = ProjectId.New();

        // Two near-identical vectors (should link) + one orthogonal (should not).
        var runOutputs = new[]
        {
            new RunOutputNode("aaaaaaaa", "## Organized run output: nightly etl", new[] { 1f, 0f, 0f }),
            new RunOutputNode("bbbbbbbb", "## Organized run output: nightly etl rerun", new[] { 0.9f, 0.1f, 0f }),
            new RunOutputNode("cccccccc", "## Organized run output: image resize", new[] { 0f, 0f, 1f }),
        };

        var tree = new DecisionTreeAssembler().Assemble(
            ProjectName.From("alpha"), Array.Empty<Decision>(), ActivityLog.Start(pid),
            Array.Empty<ToolActivity>(), runOutputs);

        // Each embedded run output is woven in as a JobRunOutput node, with the markdown header stripped.
        Assert.Equal(3, tree.Nodes.Count(n => n.Kind == TreeNodeKind.JobRunOutput));
        Assert.Contains(tree.Nodes, n => n.Kind == TreeNodeKind.JobRunOutput && n.Label == "Organized run output: nightly etl");

        // Exactly one semantic cross-link: the two near-identical outputs; the orthogonal one stays unlinked.
        var crossLinks = tree.Edges.Count(e =>
            e.ParentId.StartsWith("runoutput:") && e.ChildId.StartsWith("runoutput:"));
        Assert.Equal(1, crossLinks);

        // The brain vocabulary surfaces the run-output nodes.
        Assert.Contains("brain", tree.Answer("show me the brain").ToLowerInvariant());
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
