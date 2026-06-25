using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Fetch a project's own requirements document (not merged with global).</summary>
public sealed record GetProjectRequirementsQuery(Guid ProjectId) : IQuery<RequirementsView>;
