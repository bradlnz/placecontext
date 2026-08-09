namespace PlaceContext.Application.Ports;

/// <summary>A bounded, short-lived view of stdout/stderr for a workload that is still executing.</summary>
public sealed record LiveWorkloadOutput(
    string Text,
    bool IsComplete,
    DateTimeOffset UpdatedAt);
