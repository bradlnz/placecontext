using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Access.Enums;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Membership;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Repositories;
using PlaceContext.Application.Access;

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
        if (!Permission.All.Contains(c.PermissionOverride))
        {
            throw new ArgumentException($"Unknown permission '{c.PermissionOverride}'.");
        }
        var member = await _members.GetMemberAsync(c.UserId, ct)
                    ?? throw new InvalidOperationException("No such member in this workspace.");

        // The default admin is the last line into /settings/* — never let an override lock them out.
        if (member.IsDefaultAdmin && c.PermissionOverride == Permission.SettingsManage && c.Allowed == false)
        {
            throw new InvalidOperationException("Cannot revoke settings.manage from the default admin.");
        }
        async Task ApplyOverrideAsync()
        {
            if (c.Allowed is { } allowed)
            {
                await _grants.UpsertAsync(c.UserId, c.PermissionOverride, allowed, ct);
                return;
            }
            await _grants.RemoveAsync(c.UserId, c.PermissionOverride, ct);
        }
        await ApplyOverrideAsync();
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
        {
            return new HashSet<string>(definition.Permissions, StringComparer.Ordinal);
        }
        return Enum.TryParse<UserRole>(roleName, out var role)
                    ? RolePermissionDefaults.GetDefaults(role)
                    : new HashSet<string>(StringComparer.Ordinal);
    }
}
