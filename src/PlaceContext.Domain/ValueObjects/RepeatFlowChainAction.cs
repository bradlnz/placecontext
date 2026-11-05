namespace PlaceContext.Domain.ValueObjects;

/// <summary>Repeat preceding stages until the payload satisfies a condition, with a durable attempt limit.</summary>
public sealed class RepeatFlowChainAction : ChainAction
{
    public const string ActionType = "repeatFlow";
    public RepeatFlowChainAction(string untilCondition, int maxAttempts = 3)
    {
        if (string.IsNullOrWhiteSpace(untilCondition) || untilCondition.Length > 500)
            throw new ArgumentException("A completion condition is required (up to 500 characters).", nameof(untilCondition));
        if (maxAttempts is < 1 or > 10)
            throw new ArgumentException("Choose between 1 and 10 total attempts.", nameof(maxAttempts));
        UntilCondition = untilCondition.Trim();
        MaxAttempts = maxAttempts;
    }
    public override string Type => ActionType;
    public override string DisplayName => "Repeat flow";
    public string UntilCondition { get; }
    public int MaxAttempts { get; }
}
