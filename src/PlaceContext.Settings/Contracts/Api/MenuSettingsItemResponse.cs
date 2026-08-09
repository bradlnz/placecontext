namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record MenuSettingsItemResponse(
    string Id,
    string DefaultLabel,
    string Label,
    int Order,
    bool Visible,
    string Section);
