namespace PlaceContext.Domain.ValueObjects;

/// <summary>Identity of a single <c>ActivityRecord</c> within a ledger.</summary>
public readonly record struct ActivityRecordId(Guid Value)
{
    public static ActivityRecordId New() => new(Guid.NewGuid());

    public static ActivityRecordId From(Guid value)
        => value == Guid.Empty
            ? throw new ArgumentException("ActivityRecordId cannot be empty.", nameof(value))
            : new ActivityRecordId(value);

    public override string ToString() => Value.ToString();
}
