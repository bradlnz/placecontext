namespace PlaceContext.Domain.ValueObjects;

/// <summary>Identity of a single <c>ChangeRecord</c> within a ledger.</summary>
public readonly record struct ChangeRecordId(Guid Value)
{
    public static ChangeRecordId New() => new(Guid.NewGuid());

    public static ChangeRecordId From(Guid value)
        => value == Guid.Empty
            ? throw new ArgumentException("ChangeRecordId cannot be empty.", nameof(value))
            : new ChangeRecordId(value);

    public override string ToString() => Value.ToString();
}
