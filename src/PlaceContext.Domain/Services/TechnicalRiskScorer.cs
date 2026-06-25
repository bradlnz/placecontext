using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain service that scores classic *technical* risk from structural graph metrics and
/// code-level metrics: TODO/FIXME density, complexity hotspots, coverage gaps, coupling (low-
/// confidence link ratio), and god-node count.
/// </summary>
public sealed class TechnicalRiskScorer
{
    private readonly double _todoDensityThreshold;

    public TechnicalRiskScorer(double todoDensityThreshold = 0.5)
        => _todoDensityThreshold = todoDensityThreshold;

    public IReadOnlyList<RiskSignal> Score(GraphMetrics graph, CodeMetrics code)
    {
        var signals = new List<RiskSignal>();

        if (code.TodoDensity >= _todoDensityThreshold)
            signals.Add(RiskSignal.Of("TODO_DENSITY", RiskKind.Technical, Severity.Low,
                $"{code.TodoFixmeCount} TODO/FIXME markers across {code.FileCount} files."));

        if (code.HighComplexityCount > 0)
            signals.Add(RiskSignal.Of("HIGH_COMPLEXITY", RiskKind.Technical, Severity.Medium,
                $"{code.HighComplexityCount} high-complexity unit(s)."));

        if (code.CoverageGap >= 0.5)
            signals.Add(RiskSignal.Of("LOW_COVERAGE", RiskKind.Technical, Severity.High,
                $"Coverage gap is {code.CoverageGap:P0}."));

        if (graph.GodNodeCount > 0)
            signals.Add(RiskSignal.Of("GOD_NODES", RiskKind.Technical, Severity.High,
                $"{graph.GodNodeCount} highly-coupled god node(s) in the graph."));

        if (graph.LowConfidenceLinkRatio >= 0.4)
            signals.Add(RiskSignal.Of("WEAK_COUPLING_SIGNAL", RiskKind.Technical, Severity.Low,
                $"{graph.LowConfidenceLinkRatio:P0} of graph links are inferred/ambiguous."));

        return signals;
    }
}
