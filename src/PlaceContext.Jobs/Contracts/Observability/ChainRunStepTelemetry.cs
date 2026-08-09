using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Ports;

public sealed record ChainRunStepTelemetry(
    int StageIndex,
    int BranchIndex,
    Guid JobId,
    string? JobName,
    Guid? RunId,
    string? Status,
    double? DurationMs);
