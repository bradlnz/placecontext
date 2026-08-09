namespace PlaceContext.Identity.Contracts.Api;

public sealed record AccessSettingsResponse(
    IReadOnlyList<AccessMemberResponse> Members,
    IReadOnlyList<AccessRoleResponse> Roles,
    IReadOnlyList<string> Permissions,
    bool CustomerPortalEnabled,
    Guid CurrentUserId);
