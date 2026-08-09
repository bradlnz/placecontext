using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

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
