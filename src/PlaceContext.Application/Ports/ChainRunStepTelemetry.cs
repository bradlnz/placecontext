namespace PlaceContext.Application.Ports;

/// <summary>One step captured from a <c>job.chain</c> activity's step summary — the job that ran at
/// a stage/branch position, its run id (once dispatched) and outcome/timing. Mirrors <see
/// cref="PlaceContext.Application.Dtos.ChainStepRunView"/> but sourced purely from OTel, like the
/// rest of this reader.</summary>
public sealed record ChainRunStepTelemetry(
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string? JobName,
    Guid? RunId,
    string? Status,
    double? DurationMs);
