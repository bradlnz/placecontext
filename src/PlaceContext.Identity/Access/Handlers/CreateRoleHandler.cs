using PlaceContext.Application.Access;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateRoleHandler : ICommandHandler<CreateRoleCommand, RoleView>
{
    private readonly IRoleDefinitionRepository _roles;
    private readonly IUnitOfWork _uow;

    public CreateRoleHandler(IRoleDefinitionRepository roles, IUnitOfWork uow)
        => (_roles, _uow) = (roles, uow);

    public async Task<RoleView> HandleAsync(CreateRoleCommand c, CancellationToken ct = default)
    {
        var name = RoleDefinitionValidation.ValidateName(c.Name);
        if (Enum.GetNames<UserRole>().Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"'{name}' is a built-in system role.");
        if (await _roles.GetByNameAsync(name, ct) is not null)
            throw new ArgumentException($"A role named '{name}' already exists.");
        var permissions = RoleDefinitionValidation.ValidatePermissions(c.Permissions);

        var created = await _roles.CreateAsync(name, permissions, ct);
        await _uow.SaveChangesAsync(ct);
        return new RoleView(created.Id, created.Name, created.IsSystem, created.Permissions, MemberCount: 0);
    }
}
