namespace PlaceContext.Domain.ValueObjects;

/// <summary>How a change moved the risk backlog (explicitly resolved vs introduced items).</summary>
public readonly record struct RiskDelta(int Resolved, int Introduced)
{
    public static readonly RiskDelta None = new(0, 0);

    public static RiskDelta From(int resolved, int introduced)
    {
        if (resolved < 0 || introduced < 0)
            throw new ArgumentException("RiskDelta counts must be non-negative.");

        return new RiskDelta(resolved, introduced);
    }

    public int Net => Introduced - Resolved;
}
