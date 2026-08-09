using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>List recorded decisions for a project.</summary>
public sealed record GetDecisionsQuery(Guid ProjectId) : IQuery<IReadOnlyList<DecisionView>>;
