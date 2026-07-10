using System.Collections.Concurrent;
using System.Threading.Channels;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>One queued chart job: the whole project (TableName null) or a single table.</summary>
public sealed record AnalyticsRefreshRequest(
    TenantInfo Tenant, Guid ProjectId, Guid OpId, string? TableName, string? Instruction);

/// <summary>
/// The Analytics tab's work queue, drained one at a time by <see cref="AnalyticsWorkerService"/>
/// (local-LLM inference is CPU-bound — parallel sweeps only slow each other down). A request is
/// a whole-project sweep or a single table, optionally steered by an instruction ("totals per
/// sensor"). In-memory by design — generation is idempotent and a request is cheap to re-issue —
/// and every request is tracked as a portal operation for the notifications pane.
/// </summary>
public sealed class AnalyticsRefreshQueue
{
    private readonly Channel<AnalyticsRefreshRequest> _channel =
        Channel.CreateUnbounded<AnalyticsRefreshRequest>();
    private readonly ConcurrentDictionary<string, bool> _pending = new();
    private readonly OperationCenter _ops;

    public AnalyticsRefreshQueue(OperationCenter ops) => _ops = ops;

    public ChannelReader<AnalyticsRefreshRequest> Reader => _channel.Reader;

    private static string Key(Guid projectId, string? table) => $"{projectId:N}/{table ?? "*"}";

    /// <summary>Queue a chart refresh. False when the same scope is already queued or running.</summary>
    public bool TryEnqueue(TenantInfo tenant, Guid projectId, string projectName = "project",
        string? tableName = null, string? instruction = null)
    {
        if (!_pending.TryAdd(Key(projectId, tableName), true))
            return false;
        var title = tableName is null
            ? $"Generate analytics charts — {projectName}"
            : $"Redraw chart — {tableName}";
        var op = _ops.Track(tenant, projectId, title, $"/project/{projectId}/analytics");
        if (_channel.Writer.TryWrite(new AnalyticsRefreshRequest(tenant, projectId, op.Id, tableName, instruction)))
            return true;
        _pending.TryRemove(Key(projectId, tableName), out _);
        _ops.MarkFailed(op.Id, "could not queue the refresh");
        return false;
    }

    /// <summary>Whether anything (sweep or single table) is queued or running for the project.</summary>
    public bool IsPending(Guid projectId)
        => _pending.Keys.Any(k => k.StartsWith(projectId.ToString("N"), StringComparison.Ordinal));

    /// <summary>Whether this specific table (or the whole-project sweep) is queued or running.</summary>
    public bool IsPending(Guid projectId, string? tableName)
        => _pending.ContainsKey(Key(projectId, tableName)) || _pending.ContainsKey(Key(projectId, null));

    /// <summary>Worker-only: mark a request finished (success or failure).</summary>
    internal void MarkDone(Guid projectId, string? tableName) => _pending.TryRemove(Key(projectId, tableName), out _);
}
