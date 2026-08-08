namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record McpSettingsResponse(
    Guid? ProjectId,
    IReadOnlyList<McpProjectView> Projects,
    IReadOnlyList<McpConnectionResponse> Connections);
