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
    private readonly IUnitOfWork _uow;

    public SetUserPermissionOverrideHandler(IMembershipService members, IUserPermissionGrantRepository grants, IUnitOfWork uow)
        => (_members, _grants, _uow) = (members, grants, uow);

    public async Task<UserPermissionsView> HandleAsync(SetUserPermissionOverrideCommand c, CancellationToken ct = default)
    {
        if (!Permission.All.Contains(c.Permission))
            throw new ArgumentException($"Unknown permission '{c.Permission}'.");
        var roleName = await _members.GetRoleAsync(c.UserId, ct)
            ?? throw new InvalidOperationException("No such member in this workspace.");

        if (c.Allowed is { } allowed) await _grants.UpsertAsync(c.UserId, c.Permission, allowed, ct);
        else await _grants.RemoveAsync(c.UserId, c.Permission, ct);
        await _uow.SaveChangesAsync(ct);

        var role = Enum.Parse<UserRole>(roleName);
        var overrides = await _grants.ListForUserAsync(c.UserId, ct);
        return new UserPermissionsView(c.UserId, roleName, PermissionMatrixBuilder.Build(role, overrides));
    }
}
