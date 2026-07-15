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
/// <param name="RunId">
/// Optional pre-allocated id for the run, letting the caller correlate its own tracking (a
/// notification entry, a chain step) with the run row before the handler returns. Null = generated.
/// </param>
/// <param name="ReplayOfRunId">
/// Optional id of a prior run to replay. When supplied, the new run executes that run's captured
/// <see cref="PlaceContext.Domain.ValueObjects.WorkloadSnapshot"/> verbatim — the exact source, input
/// payloads, env, concurrency, and network policy it ran with — rather than the job's current spec,
/// so the run reproduces faithfully regardless of later job edits. <paramref name="InputPayload"/> is
/// ignored when replaying. The job's exit-code policy and timeout (not captured in the snapshot) are
/// taken from the current job.
/// </param>
public sealed record RunJobCommand(Guid JobId, string? InputPayload = null, Guid? RunId = null, Guid? ReplayOfRunId = null)
    : ICommand<JobRunDetailView>;

/// <summary>Replay a prior run: re-execute the exact workload snapshot it captured. Returns the new run.</summary>
public sealed record ReplayRunCommand(Guid RunId, Guid? NewRunId = null) : ICommand<JobRunDetailView>;
