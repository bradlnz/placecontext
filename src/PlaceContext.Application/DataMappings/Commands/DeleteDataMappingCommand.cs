using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Removes an edge from the project's data map. Ingested rows are left in place.</summary>
public sealed record DeleteDataMappingCommand(Guid MappingId) : ICommand<bool>;
