namespace PlaceContext.Projects.Api;

public sealed record ProjectPageResponse(
    ProjectPageOverviewResponse Overview,
    ProjectPageTimelineResponse? Timeline,
    IReadOnlyList<ProjectPageDecisionResponse>? Decisions,
    ProjectPageRequirementsResponse? Requirements,
    string? Message);
