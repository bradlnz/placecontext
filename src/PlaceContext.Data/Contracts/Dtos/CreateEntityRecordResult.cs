using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>The CRM create outcome: the row is always kept — existing identity values only warn.</summary>
public sealed record CreateEntityRecordResult(IReadOnlyList<string> DuplicateWarnings);
