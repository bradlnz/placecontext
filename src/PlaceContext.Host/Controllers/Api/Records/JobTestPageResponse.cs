namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobTestPageResponse(
    IReadOnlyList<JobTestJobResponse> Jobs,
    IReadOnlyList<JobTestBlockResponse> Tests);
