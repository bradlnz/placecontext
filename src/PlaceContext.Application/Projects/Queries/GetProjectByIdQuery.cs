using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns a single project by id (tenant-scoped by the repository's query filter), or null
/// when it doesn't exist or belongs to a different tenant. Backs the management API's project GET.</summary>
public sealed record GetProjectByIdQuery(Guid ProjectId) : IQuery<ProjectSummaryView?>;
