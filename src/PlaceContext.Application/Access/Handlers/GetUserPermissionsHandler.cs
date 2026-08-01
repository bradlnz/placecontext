using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetUserPermissionsHandler : IQueryHandler<GetUserPermissionsQuery, UserPermissionsView>
{
    private readonly IMembershipService _members;
    private readonly IUserPermissionGrantRepository _grants;
    private readonly IRoleDefinitionRepository _roles;

    public GetUserPermissionsHandler(
        IMembershipService members, IUserPermissionGrantRepository grants, IRoleDefinitionRepository roles)
        => (_members, _grants, _roles) = (members, grants, roles);

    public async Task<UserPermissionsView> HandleAsync(GetUserPermissionsQuery q, CancellationToken ct = default)
    {
        var member = await _members.GetMemberAsync(q.UserId, ct)
            ?? throw new InvalidOperationException("No such member in this workspace.");
        var roleGrants = await RoleGrantsAsync(member.Role, ct);
        var overrides = await _grants.ListForUserAsync(q.UserId, ct);
        return new UserPermissionsView(
            q.UserId, member.Role, PermissionMatrixBuilder.Build(roleGrants, overrides, member.IsDefaultAdmin));
    }

    /// <summary>The role's grant set from role_definitions, falling back to the hardcoded
    /// <see cref="RolePermissionDefaults"/> mapping when no row exists for the name.</summary>
    private async Task<IReadOnlySet<string>> RoleGrantsAsync(string roleName, CancellationToken ct)
    {
        var definition = await _roles.GetByNameAsync(roleName, ct);
        if (definition is not null)
            return new HashSet<string>(definition.Permissions, StringComparer.Ordinal);
        return Enum.TryParse<UserRole>(roleName, out var role)
            ? RolePermissionDefaults.GetDefaults(role)
            : new HashSet<string>(StringComparer.Ordinal);
    }
}
