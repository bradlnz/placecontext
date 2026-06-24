namespace PlaceContext.Domain.ValueObjects;

/// <summary>A normalized, lower-cased label for matching graph nodes by name.</summary>
public readonly record struct NormLabel(string Value)
{
    public static NormLabel From(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("NormLabel must not be empty.", nameof(value))
            : new NormLabel(value.Trim().ToLowerInvariant());

    public override string ToString() => Value;
}
