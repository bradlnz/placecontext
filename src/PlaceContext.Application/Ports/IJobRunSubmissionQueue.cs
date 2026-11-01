namespace PlaceContext.Application.Ports;

/// <summary>Durable receipt for a standalone job submitted by a managed Core API client.</summary>
public sealed record JobRunSubmission(Guid TrackingId, Guid ProjectId, Guid JobId, Guid RunId,
    string Status, int Attempts, string? Error, DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

/// <summary>Accepts standalone jobs without coupling their execution to an HTTP request.
/// Repeating a RunId for the same tenant and job is idempotent.</summary>
public interface IJobRunSubmissionQueue
{
    Task<JobRunSubmission> EnqueueAsync(Guid projectId, Guid jobId, string? inputPayload,
        Guid? runId = null, CancellationToken ct = default);

    Task<JobRunSubmission?> GetAsync(Guid projectId, Guid jobId, Guid trackingId,
        CancellationToken ct = default);
}
