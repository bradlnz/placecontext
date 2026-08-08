namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ProjectPageRequirementsResponse(
    string Markdown,
    DateTimeOffset? UpdatedAt,
    string? UpdatedAtDisplay);
