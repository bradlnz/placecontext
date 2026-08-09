namespace PlaceContext.Identity.Contracts.Api;

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
