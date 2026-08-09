using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Contracts.Api;

public sealed record ObservabilityPageResponse(
    IReadOnlyList<RunReportView> Runs,
    IReadOnlyList<ChainRunReportView> Chains,
    IReadOnlyList<JobRunTelemetry> LiveTraces,
    bool CanReplay);
