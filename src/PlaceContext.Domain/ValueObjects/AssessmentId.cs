namespace PlaceContext.Domain.ValueObjects;

/// <summary>Identity of an immutable <c>DebtAssessment</c> snapshot.</summary>
public readonly record struct AssessmentId(Guid Value)
{
    public static AssessmentId New() => new(Guid.NewGuid());

    public static AssessmentId From(Guid value)
        => value == Guid.Empty
            ? throw new ArgumentException("AssessmentId cannot be empty.", nameof(value))
            : new AssessmentId(value);

    public override string ToString() => Value.ToString();
}
