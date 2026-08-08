using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record ChainGateManifest(string Type, double? DurationSeconds = null, string? Expression = null);
