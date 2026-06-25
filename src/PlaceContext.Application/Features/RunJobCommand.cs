using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Executes all shards of a job (map phase) with the configured concurrency bound, optionally runs the
/// reduce step, aggregates status via the job's exit-code policy, and persists the run + all artifacts.
/// Completed run artifacts are also appended to the project's context so the existing generic
/// generation layer can see them.
/// </summary>
public sealed record RunJobCommand(Guid JobId) : ICommand<JobRunDetailView>;
