namespace PlaceContext.Application.Dtos;

public sealed record ChainGateManifest(string Type, double? DurationSeconds = null, string? Expression = null, string? ReviewQuestion = null);
