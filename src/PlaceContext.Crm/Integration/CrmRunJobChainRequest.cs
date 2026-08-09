namespace PlaceContext.Crm.Integration;

public sealed record CrmRunJobChainRequest(
    Guid ProjectId,
    Guid ChainId,
    string? InputPayload,
    Guid? ChainRunId = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null,
    Guid? CrmClientId = null);
