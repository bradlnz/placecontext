namespace PlaceContext.Application.Ports;

/// <summary>
/// One job run captured from the OTel <c>job.run</c> activity, reduced to the fields the UI wants —
/// avoids re-reading raw <see cref="System.Diagnostics.Activity"/> tags on every render.
/// </summary>
public sealed record JobRunTelemetry(
    Guid RunId,
    Guid JobId,
    string? JobName,
    Guid? ProjectId,
    string? Status,
    bool Replay,
    DateTimeOffset StartedAt,
    double? DurationMs,
    IReadOnlyList<ShardTelemetry> Shards,
    string? TraceId = null,
    string? SpanId = null);
