using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain service: folds a set of weighted <see cref="RiskSignal"/>s into a single normalized
/// <see cref="RiskScore"/>. Uses a saturating curve so risk approaches — but never exceeds — 1.0 as
/// signals accumulate, keeping a busy project from pinning at Critical on a single bad change.
/// </summary>
public sealed class RiskScoreCalculator
{
    /// <summary>Weighted-signal mass at which the score reaches ~0.63; tunes how fast risk saturates.</summary>
    private readonly double _scale;

    public RiskScoreCalculator(double scale = 8.0)
    {
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        _scale = scale;
    }

    public RiskScore Calculate(IEnumerable<RiskSignal> signals)
    {
        var mass = signals.Sum(s => s.Weight);
        if (mass <= 0) return RiskScore.Zero;

        // Saturating: 1 - e^(-mass/scale) ∈ [0,1).
        var value = 1.0 - Math.Exp(-mass / _scale);
        return RiskScore.From(value);
    }
}
