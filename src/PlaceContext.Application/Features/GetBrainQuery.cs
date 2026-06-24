using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>The organisation's "brain": every project's dependency graph joined into one graph.</summary>
public sealed record GetBrainQuery : IQuery<GraphVizView>;
