namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record EventTypePageResponse(
    string Name,
    string? Description,
    bool IsBuiltIn,
    string? PayloadSchema);
