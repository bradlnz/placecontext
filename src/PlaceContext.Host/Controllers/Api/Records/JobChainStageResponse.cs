namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobChainStageResponse(
    IReadOnlyList<JobChainJobResponse> Jobs,
    JobChainGateResponse? Gate,
    JobChainActionResponse? Action);
