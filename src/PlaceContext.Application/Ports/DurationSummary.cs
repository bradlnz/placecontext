namespace PlaceContext.Application.Ports;

/// <summary>Count + min/max/avg summary of a histogram instrument since process start.</summary>
public sealed record DurationSummary(long Count, double MinMs, double MaxMs, double AvgMs);
