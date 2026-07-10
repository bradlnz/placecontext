using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns all job definitions for a project.</summary>
public sealed record ListJobsQuery(Guid ProjectId) : IQuery<IReadOnlyList<JobView>>;
