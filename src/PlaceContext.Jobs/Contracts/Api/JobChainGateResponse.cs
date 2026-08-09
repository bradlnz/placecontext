namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobChainGateResponse(string Type, double? DurationSeconds, string? Expression);
