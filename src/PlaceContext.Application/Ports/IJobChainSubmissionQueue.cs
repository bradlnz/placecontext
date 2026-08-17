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
    DateTimeOffset? FinishedAt);

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
        CancellationToken ct = default);

    Task<JobChainSubmission?> GetAsync(Guid trackingId, CancellationToken ct = default);
}
