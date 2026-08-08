namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record LocalitySettingsResponse(
    string TimeZoneId,
    IReadOnlyList<string> TimeZones);
