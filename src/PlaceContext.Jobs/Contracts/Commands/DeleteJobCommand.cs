using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// Permanently deletes a job definition. Returns true if it existed. Mirrors
/// <see cref="DeleteJobChainCommand"/>: it does not cascade to the job's run history, artifacts, data
/// mappings, or triggers (those rows are keyed by JobId with no FK constraint, so they simply become
/// orphaned history) — callers that want a clean slate should delete triggers/chains referencing the
/// job first.
/// </summary>
public sealed record DeleteJobCommand(Guid JobId) : ICommand<bool>;
