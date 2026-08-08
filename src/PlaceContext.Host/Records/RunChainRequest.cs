namespace PlaceContext.Host.Controllers;

public sealed record RunChainRequest(
    string? InputPayload,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides);
