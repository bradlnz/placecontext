using PlaceContext.Jobs.Contracts.Management;

namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobsPageResponse(
    IReadOnlyList<JobResponse> Jobs,
    IReadOnlyList<JobsPageTriggerResponse> Triggers);
