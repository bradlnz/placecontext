namespace PlaceContext.Domain.ValueObjects;

/// <summary>Tokens consumed by an LLM call attributed to a project, by model. Counts are non-negative.</summary>
public readonly record struct TokenUsage(string Model, long InputTokens, long OutputTokens)
{
    public static TokenUsage From(string model, long inputTokens, long outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0)
            throw new ArgumentException("Token counts must be non-negative.");

        return new TokenUsage(string.IsNullOrWhiteSpace(model) ? "unknown" : model.Trim(), inputTokens, outputTokens);
    }

    public long Total => InputTokens + OutputTokens;
}
