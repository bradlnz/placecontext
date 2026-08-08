namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobTestCodePageResponse(
    JobTestBlockResponse Test,
    IReadOnlyList<JobTestRuntimeResponse> Runtimes);
