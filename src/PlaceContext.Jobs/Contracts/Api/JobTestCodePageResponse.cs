namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobTestCodePageResponse(
    JobTestBlockResponse Test,
    IReadOnlyList<JobTestRuntimeResponse> Runtimes);
