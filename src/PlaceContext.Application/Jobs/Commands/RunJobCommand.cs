using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Executes all shards of a job (map phase) with the configured concurrency bound, optionally runs the
/// reduce step, aggregates status via the job's exit-code policy, and persists the run + all artifacts.
/// Completed run artifacts are also appended to the project's context so the existing generic
/// generation layer can see them.
/// </summary>
/// <param name="InputPayload">
/// Optional override for the run's input. When supplied (by a portal modal collecting the job's
/// declared parameters, or by an event source injecting form fields), it replaces the job's stored
/// shard payloads with a single shard carrying this payload. Null = run the job's stored payloads.
/// </param>
public sealed record RunJobCommand(Guid JobId, string? InputPayload = null) : ICommand<JobRunDetailView>;
