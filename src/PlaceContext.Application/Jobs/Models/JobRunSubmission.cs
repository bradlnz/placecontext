namespace PlaceContext.Application.Jobs;

/// <summary>Durable receipt for a standalone job submitted by a managed Core API client.</summary>
public sealed record JobRunSubmission(Guid TrackingId, Guid ProjectId, Guid JobId, Guid RunId,
    string Status, int Attempts, string? Error, DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
