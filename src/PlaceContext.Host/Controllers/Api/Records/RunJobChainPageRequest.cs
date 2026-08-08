namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record RunJobChainPageRequest(
    string? InputPayload,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides);
