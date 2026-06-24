using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>List recorded decisions for a project.</summary>
public sealed record GetDecisionsQuery(Guid ProjectId) : IQuery<IReadOnlyList<DecisionView>>;
