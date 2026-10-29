namespace PlaceContext.Application.Ports;

/// <summary>
/// One chain run captured from the OTel <c>job.chain</c> activity, reduced to the fields the UI
/// wants. <see cref="Steps"/> is the chain's own step summary (stage/branch position, run id,
/// outcome) attached to the activity as a tag when the chain finishes — see
/// <c>RunJobChainHandler</c> and the Infrastructure collector for how it's captured.
/// </summary>
public sealed record ChainRunTelemetry(
    Guid ChainRunId,
    Guid ChainId,
    string? ChainName,
    Guid? ProjectId,
    string? Status,
    DateTimeOffset StartedAt,
    double? DurationMs,
    IReadOnlyList<ChainRunStepTelemetry> Steps);
