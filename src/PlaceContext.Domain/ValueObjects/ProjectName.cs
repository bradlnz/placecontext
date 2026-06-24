namespace PlaceContext.Domain.ValueObjects;

/// <summary>Human-readable name of a project. Immutable, trimmed, validated at construction.</summary>
public sealed record ProjectName
{
    public string Value { get; }

    private ProjectName(string value) => Value = value;

    public static ProjectName From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ProjectName must not be empty.", nameof(value));

        return new ProjectName(value.Trim());
    }

    public override string ToString() => Value;
}
