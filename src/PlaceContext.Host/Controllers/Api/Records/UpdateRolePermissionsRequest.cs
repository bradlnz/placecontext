namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
