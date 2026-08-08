namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record MenuSettingsResponse(IReadOnlyList<MenuSettingsItemResponse> Workspace);
