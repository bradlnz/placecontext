using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Contracts.Api;

public sealed record ObservabilityRunDetailsResponse(
    JobRunTelemetry? Telemetry,
    IReadOnlyList<TraceSpanNode> TraceSpans);
