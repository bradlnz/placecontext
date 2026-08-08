namespace PlaceContext.Host.Api;

public sealed record CoreJobParameter(
    string Name,
    string? Label,
    bool Required,
    string Type,
    IReadOnlyList<string>? Options);
