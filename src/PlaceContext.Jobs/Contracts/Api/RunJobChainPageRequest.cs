namespace PlaceContext.Jobs.Contracts.Api;

public sealed record RunJobChainPageRequest(
    string? InputPayload,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides);
