using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Ports;

public sealed record DurationSummary(long Count, double MinMs, double MaxMs, double AvgMs);
