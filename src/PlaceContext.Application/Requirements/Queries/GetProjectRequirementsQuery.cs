using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Fetch a project's own requirements document (not merged with global).</summary>
public sealed record GetProjectRequirementsQuery(Guid ProjectId) : IQuery<RequirementsView>;
