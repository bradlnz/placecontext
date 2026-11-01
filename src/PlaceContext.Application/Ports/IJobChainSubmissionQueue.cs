namespace PlaceContext.Application.Ports;

/// <summary>
/// Durable acknowledgement for an asynchronous job-chain submission made through MCP. The tracking
/// id identifies the submission receipt; the pre-allocated chain-run id identifies the actual run as
/// soon as a worker starts it.
/// </summary>
public sealed record JobChainSubmission(
    Guid TrackingId,
    Guid ProjectId,
    Guid ChainId,
    Guid ChainRunId,
    string Status,
    int Attempts,
    string? Error,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public DateTimeOffset? AvailableAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public IReadOnlyList<Guid> PreviousRunIds { get; init; } = [];
}

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

    Task<JobChainSubmission?> GetAsync(Guid trackingId, CancellationToken ct = default);
}
