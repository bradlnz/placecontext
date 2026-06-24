using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

// ---- Dependency-graph projection (Project deep-dive) ----

public sealed record GetGraphVizQuery(Guid ProjectId) : IQuery<GraphVizView>;
