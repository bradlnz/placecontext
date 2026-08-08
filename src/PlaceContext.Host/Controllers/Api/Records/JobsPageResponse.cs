using PlaceContext.Jobs.Contracts.Management;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobsPageResponse(
    IReadOnlyList<JobResponse> Jobs,
    IReadOnlyList<JobsPageTriggerResponse> Triggers);
