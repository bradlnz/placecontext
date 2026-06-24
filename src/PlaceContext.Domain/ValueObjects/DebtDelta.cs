namespace PlaceContext.Domain.ValueObjects;

/// <summary>How a change moved the debt backlog (explicitly resolved vs introduced items).</summary>
public readonly record struct DebtDelta(int Resolved, int Introduced)
{
    public static readonly DebtDelta None = new(0, 0);

    public static DebtDelta From(int resolved, int introduced)
    {
        if (resolved < 0 || introduced < 0)
            throw new ArgumentException("DebtDelta counts must be non-negative.");

        return new DebtDelta(resolved, introduced);
    }

    public int Net => Introduced - Resolved;
}
