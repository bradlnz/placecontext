using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class DebtScoringTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    private static ChangeRecord AgentChange(
        bool tests = false, bool rationale = false,
        bool archReview = false, bool liveVerified = false,
        params string[] nodes)
    {
        var ledger = ChangeLedger.Start(ProjectId.New());
        return ledger.Append(
            "summary",
            Author.Agent("claude"),
            rationale ? Rationale.Of("why") : Rationale.None,
            tests ? TestDelta.From(2, 0, 0) : TestDelta.None,
            DebtDelta.None,
            new ChangeVerification(archReview, liveVerified),
            new[] { "x.cs" },
            nodes.Select(GraphNodeId.From),
            T0);
    }

    [Fact]
    public void Untrusted_agent_change_fires_all_process_signals()
    {
        var change = AgentChange(); // no tests, no rationale, no review, not verified
        var signals = new AgenticDebtScorer().Score(change, Array.Empty<GodNode>(), reTouchedWithinWindow: false);

        Assert.Contains(signals, s => s.Code == "AGENT_NO_TESTS");
        Assert.Contains(signals, s => s.Code == "AGENT_NO_RATIONALE");
        Assert.Contains(signals, s => s.Code == "NO_ARCH_REVIEW");
        Assert.Contains(signals, s => s.Code == "NOT_LIVE_VERIFIED");
        Assert.All(signals, s => Assert.Equal(DebtKind.Agentic, s.Kind));
    }

    [Fact]
    public void Fully_trusted_agent_change_fires_no_signals()
    {
        var change = AgentChange(tests: true, rationale: true, archReview: true, liveVerified: true);
        var signals = new AgenticDebtScorer().Score(change, Array.Empty<GodNode>(), reTouchedWithinWindow: false);
        Assert.Empty(signals);
    }

    [Fact]
    public void Touching_a_god_node_fires_a_signal()
    {
        var change = AgentChange(tests: true, rationale: true, archReview: true, liveVerified: true, nodes: "auth");
        var gods = new[] { GodNode.Of(GraphNodeId.From("auth"), NormLabel.From("Auth"), 30) };
        var signals = new AgenticDebtScorer().Score(change, gods, reTouchedWithinWindow: false);
        Assert.Contains(signals, s => s.Code == "TOUCHES_GOD_NODE");
    }

    [Fact]
    public void Retouch_flag_fires_a_signal()
    {
        var change = AgentChange(tests: true, rationale: true, archReview: true, liveVerified: true);
        var signals = new AgenticDebtScorer().Score(change, Array.Empty<GodNode>(), reTouchedWithinWindow: true);
        Assert.Contains(signals, s => s.Code == "RE_TOUCHED");
    }

    [Fact]
    public void TechnicalDebtScorer_flags_god_nodes_and_low_coverage()
    {
        var graph = GraphMetrics.From(100, 300, godNodeCount: 3, averageDegree: 6, lowConfidenceLinkRatio: 0.5);
        var code = CodeMetrics.From(todoFixmeCount: 40, highComplexityCount: 5, coverageGap: 0.7, fileCount: 50);
        var signals = new TechnicalDebtScorer().Score(graph, code);

        Assert.Contains(signals, s => s.Code == "GOD_NODES");
        Assert.Contains(signals, s => s.Code == "LOW_COVERAGE");
        Assert.Contains(signals, s => s.Code == "HIGH_COMPLEXITY");
        Assert.All(signals, s => Assert.Equal(DebtKind.Technical, s.Kind));
    }

    [Fact]
    public void Calculator_is_zero_for_no_signals_and_saturates_upward()
    {
        var calc = new DebtScoreCalculator();
        Assert.Equal(0.0, calc.Calculate(Array.Empty<DebtSignal>()).Value);

        var few = calc.Calculate(new[] { DebtSignal.Of("A", DebtKind.Agentic, Severity.Low, "e") });
        var many = calc.Calculate(Enumerable.Range(0, 6)
            .Select(i => DebtSignal.Of($"S{i}", DebtKind.Agentic, Severity.Critical, "e")));

        Assert.True(many.Value > few.Value);
        Assert.True(many.Value < 1.0); // saturating, never pins
    }

    [Fact]
    public void Staleness_when_code_modified_after_note()
    {
        var note = ContextNote.Write(ProjectId.New(), "title", "body",
            new[] { GraphNodeId.From("auth") }, Digest('a'), T0);
        var policy = new ContextStalenessPolicy();

        Assert.True(policy.IsStale(note, T0.AddHours(1), Digest('a')));   // code newer
        Assert.True(policy.IsStale(note, T0.AddHours(-1), Digest('b')));  // digest drifted
        Assert.False(policy.IsStale(note, T0.AddHours(-1), Digest('a'))); // unchanged
    }

    private static ContentDigest Digest(char c) => ContentDigest.From(new string(c, 64));
}
