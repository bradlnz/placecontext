namespace PlaceContext.Application.Build;

public sealed class OutcomeReviewThrottledException(int statusCode, TimeSpan retryAfter)
    : InvalidOperationException($"AI verification returned HTTP {statusCode}; the provider needs a cooldown.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
