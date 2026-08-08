using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record AccessSettingsResponse(
    IReadOnlyList<MemberView> Members,
    IReadOnlyList<RoleView> Roles,
    IReadOnlyList<string> Permissions,
    bool CustomerPortalEnabled,
    Guid CurrentUserId);
