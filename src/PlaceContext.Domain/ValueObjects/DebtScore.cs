namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// A normalized debt score in [0,1] with its derived <see cref="DebtBand"/>. Value Object:
/// immutable, clamped and banded at construction. 0 = pristine, 1 = maximally indebted.
/// </summary>
public sealed record DebtScore
{
    public double Value { get; }
    public DebtBand Band { get; }

    private DebtScore(double value, DebtBand band)
    {
        Value = value;
        Band = band;
    }

    public static readonly DebtScore Zero = From(0.0);

    public static DebtScore From(double value)
    {
        if (double.IsNaN(value))
            throw new ArgumentException("DebtScore value must be a number.", nameof(value));

        var clamped = Math.Clamp(value, 0.0, 1.0);
        return new DebtScore(clamped, BandFor(clamped));
    }

    private static DebtBand BandFor(double v) => v switch
    {
        < 0.25 => DebtBand.Low,
        < 0.50 => DebtBand.Moderate,
        < 0.75 => DebtBand.High,
        _ => DebtBand.Critical
    };

    public override string ToString() => $"{Value:0.00} ({Band})";
}
