namespace PlaceContext.Domain.ValueObjects;

/// <summary>Identity of a <c>Decision</c> (ADR-lite).</summary>
public readonly record struct DecisionId(Guid Value)
{
    public static DecisionId New() => new(Guid.NewGuid());

    public static DecisionId From(Guid value)
        => value == Guid.Empty
            ? throw new ArgumentException("DecisionId cannot be empty.", nameof(value))
            : new DecisionId(value);

    public override string ToString() => Value.ToString();
}
