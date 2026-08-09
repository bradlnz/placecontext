namespace PlaceContext.Projects.Api;

public sealed record ProjectPageTimelineResponse(IReadOnlyList<ProjectPageChangeResponse> Changes);
