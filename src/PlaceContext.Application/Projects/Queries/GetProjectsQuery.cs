using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>List all known projects (the portal home).</summary>
public sealed record GetProjectsQuery : IQuery<IReadOnlyList<ProjectSummaryView>>;
