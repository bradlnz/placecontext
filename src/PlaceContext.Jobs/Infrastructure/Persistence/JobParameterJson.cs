namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal sealed record JobParameterJson(
    string Name,
    string? Label,
    bool Required,
    string Type = "text",
    List<string>? Options = null);
