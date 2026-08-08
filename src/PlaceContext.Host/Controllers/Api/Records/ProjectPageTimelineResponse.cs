namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectPageTimelineResponse(IReadOnlyList<ProjectPageChangeResponse> Changes);
