using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteRoleHandler : ICommandHandler<DeleteRoleCommand, bool>
{
    private readonly IRoleDefinitionRepository _roles;
    private readonly IMembershipService _members;
    private readonly IUnitOfWork _uow;

    public DeleteRoleHandler(IRoleDefinitionRepository roles, IMembershipService members, IUnitOfWork uow)
        => (_roles, _members, _uow) = (roles, members, uow);

    public async Task<bool> HandleAsync(DeleteRoleCommand c, CancellationToken ct = default)
    {
        var definition = await _roles.GetByIdAsync(c.RoleId, ct)
            ?? throw new InvalidOperationException("No such role in this workspace.");
        if (definition.IsSystem)
            throw new InvalidOperationException("Built-in system roles cannot be deleted.");
        var members = await _members.ListMembersAsync(ct);
        if (members.Any(m => string.Equals(m.Role, definition.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Role '{definition.Name}' is still assigned to members.");

        await _roles.DeleteAsync(c.RoleId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
