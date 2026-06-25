using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns a single job definition (with its full source file set), or null if not found.</summary>
public sealed record GetJobQuery(Guid JobId) : IQuery<JobView?>;
