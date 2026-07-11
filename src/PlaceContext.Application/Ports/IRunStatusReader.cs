using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Compact status projection of one job run — no shard/artifact payloads.</summary>
public sealed record JobRunStatusRow(
    Guid RunId,
    Guid JobId,
    string JobName,
    Guid ProjectId,
    JobRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Compact status projection of one chain run, with just enough step detail to
/// describe progress and to identify the job runs the chain owns.</summary>
public sealed record ChainRunStatusRow(
    Guid RunId,
    Guid ChainId,
    string ChainName,
    Guid ProjectId,
    ChainRunStatus Status,
    int TotalSteps,
    int FinishedSteps,
    string? CurrentStepName,
    IReadOnlyList<Guid> StepRunIds,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Read-side port for the run-status watcher: the persisted truth about which runs are in flight
/// and which recently reached a terminal status. Tenant-scoped like every repository query.
/// Both queries return every non-terminal run plus the terminal runs whose
/// <c>FinishedAt</c> is at or after <paramref name="finishedSince"/>.
/// </summary>
public interface IRunStatusReader
{
    Task<IReadOnlyList<JobRunStatusRow>> ListJobRunsAsync(DateTimeOffset finishedSince, CancellationToken ct = default);
    Task<IReadOnlyList<ChainRunStatusRow>> ListChainRunsAsync(DateTimeOffset finishedSince, CancellationToken ct = default);
}
