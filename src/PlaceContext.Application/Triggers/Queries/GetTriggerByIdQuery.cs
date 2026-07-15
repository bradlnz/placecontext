using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns a single trigger by id, or null if not found. Backs the management API's schedule GET.</summary>
public sealed record GetTriggerByIdQuery(Guid TriggerId) : IQuery<TriggerView?>;
