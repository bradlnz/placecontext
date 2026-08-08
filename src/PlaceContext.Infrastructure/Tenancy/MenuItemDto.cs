namespace PlaceContext.Infrastructure.Tenancy;

internal sealed record MenuItemDto(
    string Id,
    string? Label,
    int Order,
    bool Visible,
    string? Section);
