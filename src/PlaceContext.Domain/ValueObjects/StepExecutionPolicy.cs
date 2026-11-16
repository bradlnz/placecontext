namespace PlaceContext.Domain.ValueObjects;

public sealed record StepExecutionPolicy
{
    public StepExecutionPolicy(int maxRetries = 0, int retryDelaySeconds = 5, int? timeoutSeconds = null)
    {
        if (maxRetries is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(maxRetries), "Retries must be between 0 and 10.");
        if (retryDelaySeconds is < 0 or > 3600) throw new ArgumentOutOfRangeException(nameof(retryDelaySeconds), "Retry delay must be between 0 and 3600 seconds.");
        if (timeoutSeconds is < 1 or > 86400) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be between 1 and 86400 seconds, or empty to use the runtime limit.");
        MaxRetries = maxRetries;
        RetryDelaySeconds = retryDelaySeconds;
        TimeoutSeconds = timeoutSeconds;
    }

    public int MaxRetries { get; }
    public int RetryDelaySeconds { get; }
    public int? TimeoutSeconds { get; }
}
