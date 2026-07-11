using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

/// <summary>Settable run-status projections; applies the same window predicate as the real reader
/// (all non-terminal rows + terminal rows finished at/after the watermark).</summary>
public sealed class FakeRunStatusReader : IRunStatusReader
{
    public List<JobRunStatusRow> JobRuns { get; } = new();
    public List<ChainRunStatusRow> ChainRuns { get; } = new();

    public Task<IReadOnlyList<JobRunStatusRow>> ListJobRunsAsync(DateTimeOffset finishedSince, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<JobRunStatusRow>>(JobRuns
            .Where(r => r.Status is JobRunStatus.Running or JobRunStatus.Queued
                || (r.FinishedAt is { } f && f >= finishedSince))
            .ToList());

    public Task<IReadOnlyList<ChainRunStatusRow>> ListChainRunsAsync(DateTimeOffset finishedSince, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ChainRunStatusRow>>(ChainRuns
            .Where(r => r.Status == ChainRunStatus.Running
                || (r.FinishedAt is { } f && f >= finishedSince))
            .ToList());
}
