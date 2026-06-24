using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain service that scores classic *technical* debt from structural graph metrics and
/// code-level metrics: TODO/FIXME density, complexity hotspots, coverage gaps, coupling (low-
/// confidence link ratio), and god-node count.
/// </summary>
public sealed class TechnicalDebtScorer
{
    private readonly double _todoDensityThreshold;

    public TechnicalDebtScorer(double todoDensityThreshold = 0.5)
        => _todoDensityThreshold = todoDensityThreshold;

    public IReadOnlyList<DebtSignal> Score(GraphMetrics graph, CodeMetrics code)
    {
        var signals = new List<DebtSignal>();

        if (code.TodoDensity >= _todoDensityThreshold)
            signals.Add(DebtSignal.Of("TODO_DENSITY", DebtKind.Technical, Severity.Low,
                $"{code.TodoFixmeCount} TODO/FIXME markers across {code.FileCount} files."));

        if (code.HighComplexityCount > 0)
            signals.Add(DebtSignal.Of("HIGH_COMPLEXITY", DebtKind.Technical, Severity.Medium,
                $"{code.HighComplexityCount} high-complexity unit(s)."));

        if (code.CoverageGap >= 0.5)
            signals.Add(DebtSignal.Of("LOW_COVERAGE", DebtKind.Technical, Severity.High,
                $"Coverage gap is {code.CoverageGap:P0}."));

        if (graph.GodNodeCount > 0)
            signals.Add(DebtSignal.Of("GOD_NODES", DebtKind.Technical, Severity.High,
                $"{graph.GodNodeCount} highly-coupled god node(s) in the graph."));

        if (graph.LowConfidenceLinkRatio >= 0.4)
            signals.Add(DebtSignal.Of("WEAK_COUPLING_SIGNAL", DebtKind.Technical, Severity.Low,
                $"{graph.LowConfidenceLinkRatio:P0} of graph links are inferred/ambiguous."));

        return signals;
    }
}
