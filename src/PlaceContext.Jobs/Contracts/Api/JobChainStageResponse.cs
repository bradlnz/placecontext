namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobChainStageResponse(
    IReadOnlyList<JobChainJobResponse> Jobs,
    JobChainGateResponse? Gate,
    JobChainActionResponse? Action);
