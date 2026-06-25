using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Returns the full detail of a single run, including all shard and reduce artifacts.</summary>
public sealed record GetJobRunQuery(Guid RunId) : IQuery<JobRunDetailView?>;
