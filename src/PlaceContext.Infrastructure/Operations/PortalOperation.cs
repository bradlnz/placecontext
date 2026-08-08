using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Operations;

/// <summary>One background portal operation, shown in the notifications pane.</summary>
public sealed class PortalOperation
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Guid TenantId { get; init; }
    public Guid? ProjectId { get; init; }
    public required string Title { get; init; }
    /// <summary>Where to go to see the result (e.g. the job's run history).</summary>
    public string? Link { get; init; }
    /// <summary>Stable identity of the work this op tracks (e.g. <c>job-run:{id:N}</c>), so an
    /// authoritative status source (the run-status watcher) converges on the same entry the
    /// initiating page created instead of duplicating it.</summary>
    public string? CorrelationKey { get; init; }
    public PortalOperationStatus Status { get; internal set; } = PortalOperationStatus.Queued;
    public DateTimeOffset QueuedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; internal set; }
    public DateTimeOffset? FinishedAt { get; internal set; }
    /// <summary>Short human outcome ("Run abc123 — Succeeded") or the failure message.</summary>
    public string? Outcome { get; internal set; }
    /// <summary>Set when an authoritative Sync lands a terminal status: the persisted truth about
    /// the work's outcome. Advisory Mark* calls (the in-process wrapper, which reports late and
    /// can even report the wrong terminal state) no longer change a sealed operation.</summary>
    internal bool Sealed { get; set; }
}
