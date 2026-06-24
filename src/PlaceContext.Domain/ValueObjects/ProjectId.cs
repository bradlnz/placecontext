namespace PlaceContext.Domain.ValueObjects;

/// <summary>Identity of a <c>Project</c>. Value Object: equality by value, self-validating.</summary>
public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.NewGuid());

    public static ProjectId From(Guid value)
        => value == Guid.Empty
            ? throw new ArgumentException("ProjectId cannot be empty.", nameof(value))
            : new ProjectId(value);

    public override string ToString() => Value.ToString();
}
