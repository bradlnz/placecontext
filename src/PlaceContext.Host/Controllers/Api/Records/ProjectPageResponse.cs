namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectPageResponse(
    ProjectPageOverviewResponse Overview,
    ProjectPageTimelineResponse? Timeline,
    IReadOnlyList<ProjectPageDecisionResponse>? Decisions,
    ProjectPageRequirementsResponse? Requirements,
    string? Message);
