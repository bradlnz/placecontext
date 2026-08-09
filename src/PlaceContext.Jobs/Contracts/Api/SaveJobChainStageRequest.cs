namespace PlaceContext.Jobs.Contracts.Api;

public sealed record SaveJobChainStageRequest(
    IReadOnlyList<Guid> JobIds,
    JobChainGateResponse? Gate,
    JobChainActionResponse? Action);
