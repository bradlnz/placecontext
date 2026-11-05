namespace PlaceContext.Application.Jobs;

/// <summary>
/// Durable, encrypted hand-off used by the asynchronous MCP chain-ingestion tools. Implementations
/// must make <paramref name="idempotencyKey"/> unique within the current tenant when it is supplied.
/// </summary>
public interface IJobChainSubmissionQueue
{
    Task<JobChainSubmission> EnqueueAsync(
        Guid projectId,
        Guid chainId,
        string? inputPayload,
        string? idempotencyKey = null,
        CancellationToken ct = default,
        Guid? queueId = null, int? messageTtlSeconds = null, int receiptRetentionDays = 30, int maxAttempts = 3, int retryDelaySeconds = 3, DateTimeOffset? availableAt = null,
        IReadOnlyDictionary<int, string>? stepPayloadOverrides = null,
        Guid? replayOriginalRunId = null, int? replayFromStepIndex = null);

    Task<string> ResumeAsync(Guid chainRunId, Guid inputRequestId, string valuesJson, CancellationToken ct = default);

    Task<string> ResumeAgentAsync(Guid chainRunId, Guid inputRequestId, CancellationToken ct = default)
        => throw new NotSupportedException("Automatic agent resumption is unavailable.");

    Task<JobChainSubmission?> GetAsync(Guid trackingId, CancellationToken ct = default);
}
