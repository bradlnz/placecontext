using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Recompute both risk dimensions for a project and store an immutable assessment snapshot.</summary>
public sealed record RecomputeRiskCommand(Guid ProjectId) : ICommand<RiskDashboardView>;
