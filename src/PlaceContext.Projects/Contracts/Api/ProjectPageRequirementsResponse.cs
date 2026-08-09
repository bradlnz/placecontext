namespace PlaceContext.Projects.Api;

public sealed record ProjectPageRequirementsResponse(
    string Markdown,
    DateTimeOffset? UpdatedAt,
    string? UpdatedAtDisplay);
