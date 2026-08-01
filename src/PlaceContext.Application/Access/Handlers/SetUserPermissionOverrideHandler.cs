using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SetUserPermissionOverrideHandler : ICommandHandler<SetUserPermissionOverrideCommand, UserPermissionsView>
{
    private readonly IMembershipService _members;
    private readonly IUserPermissionGrantRepository _grants;
    private readonly IRoleDefinitionRepository _roles;
    private readonly IUnitOfWork _uow;

    public SetUserPermissionOverrideHandler(
        IMembershipService members, IUserPermissionGrantRepository grants, IRoleDefinitionRepository roles, IUnitOfWork uow)
        => (_members, _grants, _roles, _uow) = (members, grants, roles, uow);

    public async Task<UserPermissionsView> HandleAsync(SetUserPermissionOverrideCommand c, CancellationToken ct = default)
    {
        if (!Permission.All.Contains(c.Permission))
            throw new ArgumentException($"Unknown permission '{c.Permission}'.");
        var member = await _members.GetMemberAsync(c.UserId, ct)
            ?? throw new InvalidOperationException("No such member in this workspace.");

        // The default admin is the last line into /settings/* — never let an override lock them out.
        if (member.IsDefaultAdmin && c.Permission == Permission.SettingsManage && c.Allowed == false)
            throw new InvalidOperationException("Cannot revoke settings.manage from the default admin.");

        if (c.Allowed is { } allowed) await _grants.UpsertAsync(c.UserId, c.Permission, allowed, ct);
        else await _grants.RemoveAsync(c.UserId, c.Permission, ct);
        await _uow.SaveChangesAsync(ct);

        var roleGrants = await RoleGrantsAsync(member.Role, ct);
        var overrides = await _grants.ListForUserAsync(c.UserId, ct);
        return new UserPermissionsView(
            c.UserId, member.Role, PermissionMatrixBuilder.Build(roleGrants, overrides, member.IsDefaultAdmin));
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
