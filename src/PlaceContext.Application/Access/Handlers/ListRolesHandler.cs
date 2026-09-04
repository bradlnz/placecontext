using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListRolesHandler : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleView>>
{
    private readonly IRoleDefinitionRepository _roles;
    private readonly IMembershipService _members;

    public ListRolesHandler(IRoleDefinitionRepository roles, IMembershipService members)
        => (_roles, _members) = (roles, members);

    public async Task<IReadOnlyList<RoleView>> HandleAsync(ListRolesQuery q, CancellationToken ct = default)
    {
        var definitions = await _roles.ListAsync(ct);
        var members = await _members.ListMembersAsync(ct);
        var counts = members
            .GroupBy(m => m.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return definitions
            .Select(d => new RoleView(d.Id, d.Name, d.IsSystem, d.Permissions, counts.GetValueOrDefault(d.Name)))
            .ToList();
    }
}