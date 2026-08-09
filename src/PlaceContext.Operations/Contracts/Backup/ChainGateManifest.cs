using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Operations.Contracts.Backup;

public sealed record ChainGateManifest(string Type, double? DurationSeconds = null, string? Expression = null);
