using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>List all known projects (the portal home).</summary>
public sealed record GetProjectsQuery : IQuery<IReadOnlyList<ProjectSummaryView>>;
