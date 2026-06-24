namespace PlaceContext.Domain.ValueObjects;

/// <summary>A git commit hash. Value Object: 7–40 hex chars, lower-cased, validated.</summary>
public sealed record CommitSha
{
    public string Value { get; }

    private CommitSha(string value) => Value = value;

    public static CommitSha From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("CommitSha must not be empty.", nameof(value));

        var v = value.Trim().ToLowerInvariant();

        if (v.Length is < 7 or > 40 || !v.All(Uri.IsHexDigit))
            throw new ArgumentException($"CommitSha must be 7–40 hex chars: '{value}'.", nameof(value));

        return new CommitSha(v);
    }

    public string Short => Value[..Math.Min(7, Value.Length)];

    public override string ToString() => Value;
}
