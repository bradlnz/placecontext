using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain service: folds a set of weighted <see cref="DebtSignal"/>s into a single normalized
/// <see cref="DebtScore"/>. Uses a saturating curve so debt approaches — but never exceeds — 1.0 as
/// signals accumulate, keeping a busy project from pinning at Critical on a single bad change.
/// </summary>
public sealed class DebtScoreCalculator
{
    /// <summary>Weighted-signal mass at which the score reaches ~0.63; tunes how fast debt saturates.</summary>
    private readonly double _scale;

    public DebtScoreCalculator(double scale = 8.0)
    {
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        _scale = scale;
    }

    public DebtScore Calculate(IEnumerable<DebtSignal> signals)
    {
        var mass = signals.Sum(s => s.Weight);
        if (mass <= 0) return DebtScore.Zero;

        // Saturating: 1 - e^(-mass/scale) ∈ [0,1).
        var value = 1.0 - Math.Exp(-mass / _scale);
        return DebtScore.From(value);
    }
}
