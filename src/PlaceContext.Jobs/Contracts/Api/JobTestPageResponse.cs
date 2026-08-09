namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobTestPageResponse(
    IReadOnlyList<JobTestJobResponse> Jobs,
    IReadOnlyList<JobTestBlockResponse> Tests);
