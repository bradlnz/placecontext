using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class UpdateRolePermissionsHandler : ICommandHandler<UpdateRolePermissionsCommand, RoleView>
{
    private readonly IRoleDefinitionRepository _roles;
    private readonly IMembershipService _members;
    private readonly IUnitOfWork _uow;

    public UpdateRolePermissionsHandler(IRoleDefinitionRepository roles, IMembershipService members, IUnitOfWork uow)
        => (_roles, _members, _uow) = (roles, members, uow);

    public async Task<RoleView> HandleAsync(UpdateRolePermissionsCommand c, CancellationToken ct = default)
    {
        var definition = await _roles.GetByIdAsync(c.RoleId, ct)
            ?? throw new InvalidOperationException("No such role in this workspace.");
        if (string.Equals(definition.Name, nameof(UserRole.Owner), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Owner role's permissions cannot be changed.");
        var permissions = RoleDefinitionValidation.ValidatePermissions(c.Permissions);

        await _roles.SetPermissionsAsync(c.RoleId, permissions, ct);
        await _uow.SaveChangesAsync(ct);

        var members = await _members.ListMembersAsync(ct);
        var memberCount = members.Count(m => string.Equals(m.Role, definition.Name, StringComparison.OrdinalIgnoreCase));
        return new RoleView(definition.Id, definition.Name, definition.IsSystem, permissions, memberCount);
    }
}
