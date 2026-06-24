using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>The latest debt dashboard for one project.</summary>
public sealed record GetDebtDashboardQuery(Guid ProjectId) : IQuery<DebtDashboardView>;
