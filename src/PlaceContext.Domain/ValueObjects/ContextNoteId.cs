namespace PlaceContext.Domain.ValueObjects;

/// <summary>Identity of a <c>ContextNote</c>.</summary>
public readonly record struct ContextNoteId(Guid Value)
{
    public static ContextNoteId New() => new(Guid.NewGuid());

    public static ContextNoteId From(Guid value)
        => value == Guid.Empty
            ? throw new ArgumentException("ContextNoteId cannot be empty.", nameof(value))
            : new ContextNoteId(value);

    public override string ToString() => Value.ToString();
}
