namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobTestMethodResponse(
    string Name,
    string Status,
    long? DurationMs,
    string? Message);
