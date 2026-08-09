using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>All of the tenant's role definitions (system + custom) with their member counts —
/// powers the "Roles & permissions" section of the Access settings UI.</summary>
public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleView>>;
