using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns run history (summary rows) for a specific job.</summary>
public sealed record ListJobRunsQuery(Guid JobId) : IQuery<IReadOnlyList<JobRunView>>;
