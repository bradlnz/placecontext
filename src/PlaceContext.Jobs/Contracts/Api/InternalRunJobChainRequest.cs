namespace PlaceContext.Jobs.Contracts.Api;

public sealed record InternalRunJobChainRequest(
    Guid ProjectId,
    string? InputPayload,
    Guid? ChainRunId = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null,
    Guid? CrmClientId = null);
