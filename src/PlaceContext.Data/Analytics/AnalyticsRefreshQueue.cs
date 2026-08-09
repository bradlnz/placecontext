using System.Collections.Concurrent;
using System.Threading.Channels;
using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Analytics;

/// <summary>
/// Service-owned queue for idempotent analytics chart generation. Requests are drained serially
/// because local model inference is CPU-bound and parallel refreshes reduce throughput.
/// </summary>
public sealed class AnalyticsRefreshQueue(IBackgroundOperationNotifier operations)
{
    private readonly Channel<AnalyticsRefreshRequest> _channel =
        Channel.CreateUnbounded<AnalyticsRefreshRequest>();
    private readonly ConcurrentDictionary<string, bool> _pending = new();

    internal ChannelReader<AnalyticsRefreshRequest> Reader => _channel.Reader;

    public bool TryEnqueue(
        TenantContext tenant,
        Guid projectId,
        string projectName = "project",
        string? tableName = null,
        string? instruction = null)
    {
        if (!_pending.TryAdd(Key(projectId, tableName), true))
            return false;

        var title = tableName is null
            ? $"Generate analytics charts — {projectName}"
            : $"Redraw chart — {tableName}";
        var operationId = operations.Track(
            tenant,
            projectId,
            title,
            $"/project/{projectId}/analytics");

        if (_channel.Writer.TryWrite(new AnalyticsRefreshRequest(
                tenant, projectId, operationId, tableName, instruction)))
            return true;

        _pending.TryRemove(Key(projectId, tableName), out _);
        operations.MarkFailed(operationId, "could not queue the refresh");
        return false;
    }

    public bool IsPending(Guid projectId) =>
        _pending.Keys.Any(key => key.StartsWith(projectId.ToString("N"), StringComparison.Ordinal));

    public bool IsPending(Guid projectId, string? tableName) =>
        _pending.ContainsKey(Key(projectId, tableName)) ||
        _pending.ContainsKey(Key(projectId, null));

    internal void MarkDone(Guid projectId, string? tableName) =>
        _pending.TryRemove(Key(projectId, tableName), out _);

    private static string Key(Guid projectId, string? tableName) =>
        $"{projectId:N}/{tableName ?? "*"}";
}
