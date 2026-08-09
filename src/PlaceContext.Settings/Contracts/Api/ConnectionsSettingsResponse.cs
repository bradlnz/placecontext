namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ConnectionsSettingsResponse(
    IReadOnlyList<ConnectionProjectView> Projects,
    IReadOnlyList<string> SslModes);
