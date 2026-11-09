using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Fetch the global requirements document.</summary>
public sealed record GetGlobalRequirementsQuery : IQuery<RequirementsView>;
