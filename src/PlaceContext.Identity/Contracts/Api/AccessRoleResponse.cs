namespace PlaceContext.Identity.Contracts.Api;

public sealed record AccessRoleResponse(
    Guid Id,
    string Name,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int MemberCount);
