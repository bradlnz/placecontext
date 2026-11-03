using Microsoft.JSInterop;
using PlaceContext.Application.Access;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ObservabilityViewModel
{
    public const string LogEditorId = "log-query";
    private bool _logEditorMounted;
    private bool _logEditorInitializing;
    private bool _disposed;
    private Task _logEditorCleanup = Task.CompletedTask;
    private List<QueryLogEvent> _logEvents = [];
    private Dictionary<string, string> _appliedLogMappings = [];

    public async Task AfterRenderAsync()
    {
        if (_disposed || _logEditorInitializing)
        {
            return;
        }
        if (!IsLogsTab)
        {
            if (_logEditorMounted)
            {
                LogQueryText = await js.InvokeAsync<string>("pcmonaco.getValue", LogEditorId);
                await DestroyLogEditorAsync();
            }
            return;
        }
        if (Loading)
        {
            return;
        }
        if (_logEditorMounted && await js.InvokeAsync<bool>("pcmonaco.isMounted", LogEditorId))
        {
            return;
        }
        _logEditorMounted = false;
        _logEditorInitializing = true;
        try
        {
            await js.InvokeAsync<bool>("pcmonaco.init", LogEditorId, LogQueryText, "logquery");
            _logEditorMounted = await js.InvokeAsync<bool>("pcmonaco.isMounted", LogEditorId);
            if (_disposed || !IsLogsTab)
            {
                await DestroyLogEditorAsync();
            }
        }
        catch (JSDisconnectedException) { }
        finally
        {
            _logEditorInitializing = false;
        }
    }

    private async Task DestroyLogEditorAsync()
    {
        if (!_logEditorMounted)
        {
            return;
        }
        _logEditorMounted = false;
        try
        {
            await js.InvokeVoidAsync("pcmonaco.destroy", LogEditorId);
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    public Dictionary<string, string> LogFieldMappings
    {
        get;
    } = new()
    {
        ["timestamp"] = "",
        ["level"] = "",
        ["message"] = "",
        ["stream"] = "",
    };
    public IReadOnlyList<string> LogJsonFields { get; private set; } = [];
    public string LogQueryText { get; set; } = LogAnalyticsQuery.DefaultQuery;
    public string? LogQueryError
    {
        get; private set;
    }
    public bool QueryingLogs
    {
        get; private set;
    }
    public bool WrapLogResults { get; set; } = true;
    public bool IncludeServiceLogs
    {
        get; set;
    }
    public bool CanReadServiceLogs
    {
        get; private set;
    }
    public int LogServiceCount
    {
        get; private set;
    }
    public string? ServiceLogsWarning
    {
        get; private set;
    }
    public IReadOnlyList<QueryLogEvent>? LogResults
    {
        get; private set;
    }
    public LogAnalyticsResult? AnalyticsResult
    {
        get; private set;
    }
    public QueryLogEvent? SelectedLog
    {
        get; private set;
    }
    public LogAnalyticsRow? SelectedLogFields
    {
        get; private set;
    }
    public IReadOnlyList<QueryLogEvent> LogContext { get; private set; } = [];
    public int LogRunCount
    {
        get; private set;
    }
    public int LogEventCount
    {
        get; private set;
    }
    public int LogMatchCount
    {
        get; private set;
    }
    public bool LogSourcesTruncated
    {
        get; private set;
    }
    public DateTimeOffset? LogsQueriedAt
    {
        get; private set;
    }
    public string LogsQueriedLabel => LogsQueriedAt?.ToUniversalTime().ToString("HH:mm:ss 'UTC'") ?? "";

    public void ToggleLogEvent(QueryLogEvent entry)
    {
        if (ReferenceEquals(SelectedLog, entry))
        {
            CloseLogEvent();
            return;
        }
        var source = _logEvents.Where(candidate =>
            candidate.Report?.Run.Id == entry.Report?.Run.Id && candidate.Service?.Id == entry.Service?.Id
            && candidate.Service?.ProjectId == entry.Service?.ProjectId && candidate.Event.Stream == entry.Event.Stream).ToList();
        var index = source.FindIndex(candidate => ReferenceEquals(candidate, entry));
        if (index < 0)
        {
            return;
        }
        SelectedLog = entry;
        SelectedLogFields = LogAnalyticsQuery.ToRow(entry, _appliedLogMappings);
        LogContext = source.Skip(Math.Max(0, index - 10)).Take(Math.Min(10, index) + 11).ToList();
    }

    public void CloseLogEvent()
    {
        SelectedLog = null;
        SelectedLogFields = null;
        LogContext = [];
    }

    public async Task SelectLogsTabAsync()
    {
        SelectTab(LogsTab);
        if (LogResults is null)
        {
            await QueryLogsAsync();
        }
    }

    public async Task UseLogExampleAsync(string query)
    {
        LogQueryText = query;
        if (_logEditorMounted)
        {
            await js.InvokeVoidAsync("pcmonaco.setValue", LogEditorId, query, "logquery");
        }
        await QueryLogsAsync();
    }

    public async Task QueryLogsAsync()
    {
        if (QueryingLogs)
        {
            return;
        }
        CloseLogEvent();
        QueryingLogs = true;
        LogQueryError = null;
        NotifyStateChanged();
        try
        {
            if (_logEditorMounted)
            {
                LogQueryText = await js.InvokeAsync<string>("pcmonaco.getValue", LogEditorId);
            }
            var query = new LogAnalyticsQuery(LogQueryText, DateTimeOffset.UtcNow);
            // shortcut: search the latest 100 runs and 2,000 events per run; add an indexed log store for longer retention.
            var recent = await service.ListRecentRunReportsAsync(take: 100);
            var reports = new List<RunReportView>();
            // The history feed contains summaries; logs must be loaded from the retained run details.
            foreach (var report in recent)
            {
                if (await service.GetJobRunAsync(report.Run.Id) is { } detail)
                {
                    reports.Add(report with
                    {
                        Run = detail
                    });
                }
            }
            var events = CollectLogEvents(reports);
            LogServiceCount = 0;
            ServiceLogsWarning = null;
            CanReadServiceLogs = serviceConsole is not null && permissions is not null && await permissions.HasAsync(Permission.SettingsManage);
            if (IncludeServiceLogs && CanReadServiceLogs)
            {
                try
                {
                    foreach (var project in await service.GetProjectsAsync())
                    {
                        foreach (var instance in await serviceConsole!.ListAsync(project.Id))
                        {
                            if (LogServiceCount >= 20)
                            {
                                ServiceLogsWarning = "Service search is limited to 20 containers and their latest 200 log lines.";
                                break;
                            }
                            var entries = ContainerLogsViewModel.Parse(await serviceConsole.LogsAsync(project.Id, instance.Id));
                            var source = new ServiceLogSource(instance.Id, project.Id, instance.Name, project.Name, instance.Status);
                            events.AddRange(entries.Select(entry => new QueryLogEvent(null, entry, source)));
                            LogEventCount += entries.Count;
                            LogServiceCount++;
                        }
                        if (LogServiceCount >= 20)
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    ServiceLogsWarning = "Some service logs could not be loaded. Check Services and retry; job logs are still available.";
                }
            }
            AnalyticsResult = query.Execute(events, LogFieldMappings);
            _logEvents = events;
            _appliedLogMappings = new(LogFieldMappings);
            LogJsonFields = AnalyticsResult.SourceFields;
            LogMatchCount = AnalyticsResult.MatchedEvents;
            LogResults = AnalyticsResult.Rows.Where(row => row.Source is not null).Select(row => row.Source!).ToList();
            LogsQueriedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            LogQueryError = ex.Message;
            LogResults = null;
            AnalyticsResult = null;
            _logEvents = [];
        }
        finally
        {
            QueryingLogs = false;
            NotifyStateChanged();
        }
    }

    public void SetLogResults(IReadOnlyList<RunReportView> reports, LogQuery query)
    {
        CloseLogEvent();
        _logEvents = CollectLogEvents(reports);
        _appliedLogMappings = [];
        var matches = _logEvents.Where(query.Matches).ToList();
        LogMatchCount = matches.Count;
        LogResults = query.Sort(matches).Take(500).ToList();
    }

    private List<QueryLogEvent> CollectLogEvents(IReadOnlyList<RunReportView> reports)
    {
        LogRunCount = reports.Count;
        LogEventCount = 0;
        LogSourcesTruncated = false;
        var events = new List<QueryLogEvent>();
        foreach (var report in reports)
        {
            var logs = new JobRunLogsViewModel(output);
            logs.SetRun(report.Run);
            LogEventCount += logs.Events.Count;
            LogSourcesTruncated |= logs.Truncated;
            events.AddRange(logs.Events.Select(entry => new QueryLogEvent(report, entry)));
        }
        return events;
    }
}
