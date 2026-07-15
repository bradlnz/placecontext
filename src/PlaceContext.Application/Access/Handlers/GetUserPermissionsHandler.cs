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

    public GetUserPermissionsHandler(IMembershipService members, IUserPermissionGrantRepository grants)
        => (_members, _grants) = (members, grants);

    public async Task<UserPermissionsView> HandleAsync(GetUserPermissionsQuery q, CancellationToken ct = default)
    {
        var roleName = await _members.GetRoleAsync(q.UserId, ct)
            ?? throw new InvalidOperationException("No such member in this workspace.");
        var role = Enum.Parse<UserRole>(roleName);
        var overrides = await _grants.ListForUserAsync(q.UserId, ct);
        return new UserPermissionsView(q.UserId, roleName, PermissionMatrixBuilder.Build(role, overrides));
    }
}
