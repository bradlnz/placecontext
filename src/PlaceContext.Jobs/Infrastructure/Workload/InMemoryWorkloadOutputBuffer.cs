using System.Collections.Concurrent;
using System.Text;
using PlaceContext.Application.Ports;

namespace PlaceContext.Jobs.Infrastructure.Workload;

/// <summary>
/// Per-host live log buffer. Each workload is capped so a noisy job cannot grow host memory without
/// bound; completed streams expire after ten minutes, long enough for the run UI to hand over to the
/// persisted job result.
/// </summary>
public sealed class InMemoryWorkloadOutputBuffer : IWorkloadOutputBuffer
{
    private const int MaxCharsPerWorkload = 256 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, WorkloadOutputEntry> _entries = new(StringComparer.Ordinal);

    public void Append(string correlationId, string text, bool isError = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var entry = _entries.GetOrAdd(correlationId, static _ => new WorkloadOutputEntry());
        lock (entry.Gate)
        {
            AppendBounded(isError ? entry.Stderr : entry.Stdout, text);
            entry.UpdatedAt = DateTimeOffset.UtcNow;
        }
        EvictExpired();
    }

    public void Set(string correlationId, string stdout, string stderr = "")
    {
        var entry = _entries.GetOrAdd(correlationId, static _ => new WorkloadOutputEntry());
        lock (entry.Gate)
        {
            entry.Stdout.Clear();
            entry.Stderr.Clear();
            AppendBounded(entry.Stdout, stdout);
            AppendBounded(entry.Stderr, stderr);
            entry.UpdatedAt = DateTimeOffset.UtcNow;
        }
        EvictExpired();
    }

    public void Complete(string correlationId)
    {
        var entry = _entries.GetOrAdd(correlationId, static _ => new WorkloadOutputEntry());
        lock (entry.Gate)
        {
            entry.IsComplete = true;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public LiveWorkloadOutput? Snapshot(Guid runId)
    {
        EvictExpired();
        var prefix = runId.ToString("N");
        var matches = _entries
            .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
        if (matches.Count == 0) return null;

        var combined = new StringBuilder();
        var complete = true;
        var updatedAt = DateTimeOffset.MinValue;
        foreach (var (key, entry) in matches)
        {
            lock (entry.Gate)
            {
                if (matches.Count > 1)
                    combined.AppendLine($"── {StreamLabel(key)} ──");
                combined.Append(entry.Stdout);
                if (entry.Stderr.Length > 0)
                {
                    if (combined.Length > 0 && combined[^1] != '\n') combined.AppendLine();
                    combined.AppendLine("[stderr]");
                    combined.Append(entry.Stderr);
                }
                if (combined.Length > 0 && combined[^1] != '\n') combined.AppendLine();
                complete &= entry.IsComplete;
                if (entry.UpdatedAt > updatedAt) updatedAt = entry.UpdatedAt;
            }
        }
        return new LiveWorkloadOutput(combined.ToString(), complete, updatedAt);
    }

    private static string StreamLabel(string correlationId)
    {
        var map = correlationId.LastIndexOf("-map-", StringComparison.Ordinal);
        if (map >= 0) return $"map {correlationId[(map + 5)..]}";
        return correlationId.EndsWith("-reduce", StringComparison.Ordinal) ? "reduce" : "workload";
    }

    private static void AppendBounded(StringBuilder builder, string text)
    {
        builder.Append(text);
        if (builder.Length <= MaxCharsPerWorkload) return;
        builder.Remove(0, builder.Length - MaxCharsPerWorkload);
    }

    private void EvictExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var (key, entry) in _entries)
        {
            lock (entry.Gate)
            {
                if (entry.IsComplete && entry.UpdatedAt < cutoff)
                    _entries.TryRemove(key, out _);
            }
        }
    }

}
