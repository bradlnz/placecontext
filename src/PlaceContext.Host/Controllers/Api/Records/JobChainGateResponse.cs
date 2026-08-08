namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobChainGateResponse(string Type, double? DurationSeconds, string? Expression);
