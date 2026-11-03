using System.Text.Json;
using System.Text.RegularExpressions;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Workload;
namespace PlaceContext.Host.Components.ViewModels;

public sealed class JobRunLogsViewModel(IWorkloadOutputBuffer output) : PageViewModel, IComponentViewModel
{
    private const int MaxEvents = 2000;
    private Guid? _runId;
    private IReadOnlyList<(string Stream, string? Text)> _sources = [];
    public string Search { get; set; } = "";
    public string Stream { get; set; } = "";
    public string Level { get; set; } = "";
    public bool Paused
    {
        get; private set;
    }
    public bool IsLive
    {
        get; private set;
    }
    public bool Truncated
    {
        get; private set;
    }
    public DateTimeOffset? UpdatedAt
    {
        get; private set;
    }
    public IReadOnlyList<JobLogEvent> Events { get; private set; } = [];
    public IEnumerable<string> Streams => Events.Select(e => e.Stream).Distinct();
    public IEnumerable<JobLogEvent> Filtered => Events.Where(e =>
        (Stream.Length == 0 || e.Stream == Stream) && (Level.Length == 0 || e.Level == Level)
        && (Search.Length == 0 || e.Message.Contains(Search, StringComparison.OrdinalIgnoreCase)));
    public string FullLog => string.Join("\n", _sources.Where(source => !string.IsNullOrEmpty(source.Text))
        .Select(source => $"── {source.Stream} ──\n{source.Text}"));

    public void SetRun(JobRunDetailView run)
    {
        if (_runId != run.Id)
        {
            _runId = run.Id;
            Paused = false;
            Stream = "";
        }
        if (Paused)
        {
            return;
        }
        IsLive = run.Status is "Running" or "Queued";
        var live = IsLive ? output.Snapshot(run.Id) : null;
        var sources = live is { Text.Length: > 0 }
            ? new List<(string Stream, string? Text)> { ("live", live.Text) }
            : run.ShardResults.OrderBy(s => s.Index).Select(s => ($"shard {s.Index}", s.Log)).ToList();
        if (live is not { Text.Length: > 0 } && run.ReduceResult is { } reduce)
        {
            sources.Add(("reduce", reduce.Log));
        }
        UpdatedAt = live?.UpdatedAt ?? run.FinishedAt;
        if (_sources.SequenceEqual(sources))
        {
            return;
        }
        _sources = sources;

        var events = new Queue<(int Index, string Stream, string Line)>();
        var count = 0;
        foreach (var (source, text) in sources)
        {
            using var reader = new StringReader(text ?? "");
            var stream = source;
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("── ") && line.EndsWith(" ──"))
                {
                    stream = line[3..^3];
                    continue;
                }
                if (line is "[stderr]" or "--- stderr ---")
                {
                    stream += " / stderr";
                    continue;
                }
                events.Enqueue((++count, stream, line));
                // shortcut: retain 2,000 events in memory; use server-side paging when retained logs outgrow memory.
                if (events.Count > MaxEvents)
                {
                    events.Dequeue();
                }
            }
        }
        Events = events.Select(entry => Parse(entry.Index, entry.Stream, entry.Line)).ToList();
        Truncated = count > MaxEvents;
        if (Stream.Length > 0 && !Streams.Contains(Stream))
        {
            Stream = "";
        }
    }

    public void TogglePause(JobRunDetailView run)
    {
        Paused = !Paused;
        if (!Paused)
        {
            SetRun(run);
        }
    }

    private static readonly Regex Prefix = new(
        @"^\s*\[?(?<time>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\]?\s*",
        RegexOptions.CultureInvariant);
    private static readonly Regex Severity = new(
        @"^\s*\[?(TRACE|DEBUG|INFO|INFORMATION|WARN|WARNING|ERROR|FATAL|CRITICAL)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static JobLogEvent Parse(int index, string stream, string line)
    {
        string? time = null;
        string? level = null;
        var timestamp = Prefix.Match(line);
        var message = line[timestamp.Length..];
        if (message.TrimStart().StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(message);
                time = ReadString(json.RootElement, "timestamp", "time", "@t");
                level = ReadString(json.RootElement, "level", "severity", "@l");
            }
            catch (JsonException) { }
        }
        if (timestamp.Success)
        {
            time ??= timestamp.Groups["time"].Value;
        }
        var severity = Severity.Match(line[timestamp.Length..]);
        if (severity.Success)
        {
            level ??= severity.Groups[1].Value;
        }
        level = level?.ToUpperInvariant() switch
        {
            "INFORMATION" => "INFO",
            "WARNING" => "WARN",
            "CRITICAL" => "FATAL",
            "TRACE" or "DEBUG" or "INFO" or "WARN" or "ERROR" or "FATAL" => level.ToUpperInvariant(),
            _ => "",
        };
        return new(index, stream, time, level, line);
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }
}
