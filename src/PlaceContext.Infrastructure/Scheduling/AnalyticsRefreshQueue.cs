using System.Collections.Concurrent;
using System.Threading.Channels;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>
/// The Analytics tab's work queue: a refresh request is (tenant, project), drained one at a time by
/// <see cref="AnalyticsWorkerService"/>. In-memory by design — chart generation is idempotent and a
/// request is cheap to re-issue, so a replica restart losing the queue costs one click. Each request
/// is also tracked as a portal operation, so the notifications pane shows the sweep's progress.
/// </summary>
public sealed class AnalyticsRefreshQueue
{
    private readonly Channel<(TenantInfo Tenant, Guid ProjectId, Guid OpId)> _channel =
        Channel.CreateUnbounded<(TenantInfo, Guid, Guid)>();
    private readonly ConcurrentDictionary<Guid, bool> _pending = new();
    private readonly OperationCenter _ops;

    public AnalyticsRefreshQueue(OperationCenter ops) => _ops = ops;

    public ChannelReader<(TenantInfo Tenant, Guid ProjectId, Guid OpId)> Reader => _channel.Reader;

    /// <summary>Queue a project's chart refresh. False when one is already queued or running.</summary>
    public bool TryEnqueue(TenantInfo tenant, Guid projectId, string projectName = "project")
    {
        if (!_pending.TryAdd(projectId, true))
            return false;
        var op = _ops.Track(tenant, projectId, $"Generate analytics charts — {projectName}",
            $"/project/{projectId}/analytics");
        if (_channel.Writer.TryWrite((tenant, projectId, op.Id)))
            return true;
        _pending.TryRemove(projectId, out _);
        _ops.MarkFailed(op.Id, "could not queue the refresh");
        return false;
    }

    /// <summary>Whether a refresh for this project is queued or running.</summary>
    public bool IsPending(Guid projectId) => _pending.ContainsKey(projectId);

    /// <summary>Worker-only: mark a project's refresh finished (success or failure).</summary>
    internal void MarkDone(Guid projectId) => _pending.TryRemove(projectId, out _);
}
