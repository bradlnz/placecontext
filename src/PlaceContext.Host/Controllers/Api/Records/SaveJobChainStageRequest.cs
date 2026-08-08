namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record SaveJobChainStageRequest(
    IReadOnlyList<Guid> JobIds,
    JobChainGateResponse? Gate,
    JobChainActionResponse? Action);
