using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Fetch a project's context document (the knowledge an agent reads before working).</summary>
public sealed record GetContextQuery(Guid ProjectId) : IQuery<ProjectContextView>;
