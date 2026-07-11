using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>All data mappings of a project — the edges of its data map canvas.</summary>
public sealed record ListDataMappingsQuery(Guid ProjectId) : IQuery<IReadOnlyList<DataMappingView>>;
