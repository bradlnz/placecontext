namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// A normalized risk score in [0,1] with its derived <see cref="RiskBand"/>. Value Object:
/// immutable, clamped and banded at construction. 0 = pristine, 1 = maximally inrisked.
/// </summary>
public sealed record RiskScore
{
    public double Value { get; }
    public RiskBand Band { get; }

    private RiskScore(double value, RiskBand band)
    {
        Value = value;
        Band = band;
    }

    public static readonly RiskScore Zero = From(0.0);

    public static RiskScore From(double value)
    {
        if (double.IsNaN(value))
            throw new ArgumentException("RiskScore value must be a number.", nameof(value));

        var clamped = Math.Clamp(value, 0.0, 1.0);
        return new RiskScore(clamped, BandFor(clamped));
    }

    private static RiskBand BandFor(double v) => v switch
    {
        < 0.25 => RiskBand.Low,
        < 0.50 => RiskBand.Moderate,
        < 0.75 => RiskBand.High,
        _ => RiskBand.Critical
    };

    public override string ToString() => $"{Value:0.00} ({Band})";
}
