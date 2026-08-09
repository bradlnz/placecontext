namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobTestMethodResponse(
    string Name,
    string Status,
    long? DurationMs,
    string? Message);
