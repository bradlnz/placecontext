namespace PlaceContext.Application.Dtos;

/// <summary>The latest framework result for one method inside a test block.</summary>
public sealed record JobTestMethodResult(
    string Name,
    string Status,
    long? DurationMs = null,
    string? Message = null);
