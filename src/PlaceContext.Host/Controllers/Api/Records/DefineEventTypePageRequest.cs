namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record DefineEventTypePageRequest(
    string Name,
    string? Description,
    string? PayloadSchema);
