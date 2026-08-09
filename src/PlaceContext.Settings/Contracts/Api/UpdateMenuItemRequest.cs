namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record UpdateMenuItemRequest(
    string Id,
    string? Label,
    bool Visible,
    string? Section);
