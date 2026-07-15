using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>The full permission matrix (role default + override + effective, for every catalog
/// permission) for one workspace member — powers the Access settings admin UI.</summary>
public sealed record GetUserPermissionsQuery(Guid UserId) : IQuery<UserPermissionsView>;
