using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Uploads (replaces) the map-step code file set of a job. Targets an existing job by
/// <see cref="JobId"/>, or by <see cref="ProjectId"/> + <see cref="JobName"/> — creating the job with
/// sensible defaults (one "{}" shard, concurrency 1, success exit code 0) when it does not yet exist.
/// Existing input payloads, env, exit-code policy, concurrency, reduce step, and network policy are
/// preserved; only the map source is swapped to the uploaded code. All content is opaque.
/// </summary>
public sealed record UploadJobCodeCommand(
    Guid? JobId,
    Guid? ProjectId,
    string? JobName,
    string RuntimeId,
    string? Entrypoint,
    IReadOnlyList<CodeFileDto> Files)
    : ICommand<JobView>;
