using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Ports;

public sealed record ChainRunTelemetry(
    Guid ChainRunId,
    Guid ChainId,
    string? ChainName,
    Guid? ProjectId,
    string? Status,
    DateTimeOffset StartedAt,
    double? DurationMs,
    IReadOnlyList<ChainRunStepTelemetry> Steps);
