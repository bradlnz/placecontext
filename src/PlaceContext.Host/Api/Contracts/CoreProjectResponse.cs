namespace PlaceContext.Host.Api;

/// <summary>Health-safe project response for frontend clients.</summary>
public sealed record CoreProjectResponse(
    Guid Id,
    string Name,
    string Path,
    string Status,
    bool IsGraphified);
