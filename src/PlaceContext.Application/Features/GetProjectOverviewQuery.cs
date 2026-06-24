using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Full overview for one project.</summary>
public sealed record GetProjectOverviewQuery(Guid ProjectId) : IQuery<ProjectOverviewView>;
