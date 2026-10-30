using System.Globalization;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ObservabilityViewModel(
    PlaceContextService service,
    PortalUiState ui,
    NavigationManager navigation,
    OperationCenter operations,
    IJobTelemetryReader telemetry,
    IWorkloadOutputBuffer output,
    IJSRuntime js,
    IServiceConsole? serviceConsole = null,
    IPermissionService? permissions = null,
    ICurrentTenant? tenant = null
) : PageViewModel, IDisposable
{
    public const string RunsTab = "runs";
    public const string ChainsTab = "workflows";
    public const string TracesTab = "traces";
    public const string LogsTab = "logs";
    private int _detailVersion;
    private IDisposable? _operationSubscription;
    private int _operationRefreshPending;
    private int _operationVersion;
    public string? RunId { get; set; }
    public string? ChainRunId { get; set; }
    public IReadOnlyList<RunReportView>? Reports { get; private set; }
    public RunReportView? Open { get; private set; }
    public IReadOnlyList<RunArtifactLinkView>? OpenArtifacts { get; private set; }
    public JobRunTelemetry? OpenTelemetry { get; private set; }
    public IReadOnlyList<TraceSpanNode>? OpenTraceSpans { get; private set; }
    public IReadOnlyList<JobRunTelemetry>? LiveTraces { get; private set; }
    public IReadOnlyList<ChainRunReportView>? ChainReports { get; private set; }
    public ChainRunReportView? OpenChain { get; private set; }
    public Guid? SelectedChainStepRunId { get; private set; }
    public JobRunDetailView? ChainStepRunDetail { get; private set; }
    public string? ChainLogsError { get; private set; }
    public IReadOnlyList<(string Path, string Value)> OpenChainContextRows =>
        OpenChain is null ? Array.Empty<(string, string)>() : FlattenContext(OpenChain.Run.FinalOutput);
    public string Tab { get; private set; } = RunsTab;
    public bool IsRunsTab => Tab == RunsTab;
    public bool IsChainsTab => Tab == ChainsTab;
    public bool IsTracesTab => Tab == TracesTab;
    public bool IsLogsTab => Tab == LogsTab;
    public string? Message { get; private set; }
    public bool Loading { get; private set; } = true;
    public int ActiveRunCount =>
        Tab switch
        {
            ChainsTab => ChainReports?.Count ?? 0,
            TracesTab => LiveTraces?.Count ?? 0,
            _ => Reports?.Count ?? 0,
        };
    public int ActiveSuccessPercent =>
        ActiveRunCount == 0
            ? 0
            : (int)Math.Round(ActiveStatusCount(JobRunStatus.Succeeded) * 100d / ActiveRunCount);

    public async Task InitializeAsync()
    {
        await _logEditorCleanup;
        _disposed = false;
        _operationSubscription?.Dispose();
        if (tenant is { IsResolved: true })
            _operationSubscription = operations.Subscribe(tenant.TenantId, OnOperationsChanged);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(navigation.Uri).Query);
        Tab = NormalizeTab(query["tab"] ?? (ChainRunId is not null ? ChainsTab : RunId is not null ? RunsTab : Tab));
        ui.Set("Observability", "cross-project job history");
        Loading = false;
        NotifyStateChanged();
        await RefreshAsync();
        await ApplyDeepLinkAsync();
        NotifyStateChanged();
    }

    public int ActiveStatusCount(string status) =>
        Tab switch
        {
            ChainsTab => ChainReports?.Count(report =>
                string.Equals(report.Run.Status, status, StringComparison.OrdinalIgnoreCase)
            )
                ?? 0,
            TracesTab => LiveTraces?.Count(trace =>
                string.Equals(trace.Status, status, StringComparison.OrdinalIgnoreCase)
            )
                ?? 0,
            _ => Reports?.Count(report =>
                string.Equals(report.Run.Status, status, StringComparison.OrdinalIgnoreCase)
            )
                ?? 0,
        };

    public int ActiveStatusCount(JobRunStatus status) => ActiveStatusCount(status.ToString());

    public int SucceededShardCount(JobRunDetailView run) =>
        run.ShardResults.Count(s =>
            ScopedPresentationCatalog.JobStatus(s.Outcome) == JobRunStatus.Succeeded
        );

    public int FailedShardCount(JobRunDetailView run) =>
        run.ShardResults.Count(s =>
            ScopedPresentationCatalog.JobStatus(s.Outcome) == JobRunStatus.Failed
        );

    public string FailedShardSummary(JobRunDetailView run) =>
        FailedShardCount(run) == 0 ? string.Empty : $" · {FailedShardCount(run)} failed";

    public int SucceededStepCount(ChainRunView run) =>
        run.Steps.Count(s =>
            ScopedPresentationCatalog.StepStatus(s.Status) == ChainStepStatus.Succeeded
        );

    private static IReadOnlyList<(string Path, string Value)> FlattenContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<(string, string)>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var rows = new List<(string, string)>();
            Flatten(doc.RootElement, "", rows);
            return rows.Take(250).ToList();
        }
        catch (JsonException)
        {
            return new[] { ("payload", json) };
        }
    }

    private static void Flatten(JsonElement node, string path, List<(string, string)> rows)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
                Flatten(
                    property.Value,
                    string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}",
                    rows
                );
            return;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            rows.Add((path, node.GetRawText()));
            return;
        }

        rows.Add((path, node.ToString()));
    }

    public bool IsFailed(string? status) =>
        ScopedPresentationCatalog.JobStatus(status) == JobRunStatus.Failed
        || ScopedPresentationCatalog.StepStatus(status) == ChainStepStatus.Failed;

    public bool IsReplay(JobRunTelemetry telemetryData) => telemetryData.Replay;

    public string TraceLabel(JobRunTelemetry trace) =>
        trace.TraceId is { Length: > 8 } id ? id[..8] : trace.TraceId ?? "—";

    public string ChainRunDuration(ChainRunView run) =>
        run.FinishedAt.HasValue
            ? $" · {Presentation.Duration(run.StartedAt, run.FinishedAt.Value)}"
            : " · running…";

    public string ChainRunStepSummary(ChainRunView run) =>
        $"{SucceededStepCount(run)}/{run.Steps.Count} step(s) succeeded";

    public void SelectRunsTab() => SelectTab(RunsTab);

    public void SelectChainsTab() => SelectTab(ChainsTab);

    public async Task RefreshAsync()
    {
        if (IsLogsTab) { await QueryLogsAsync(); return; }
        Message = null;
        var tab = Tab;
        try
        {
            if (tab == RunsTab)
                Reports = await service.ListRecentRunReportsAsync(take: 50);
            else if (tab == TracesTab)
                LiveTraces = telemetry.RecentRuns(50);
            else if (tab == ChainsTab)
            {
                ChainReports = await service.ListRecentChainRunsAsync(take: 50);
                if (OpenChain is { } openChain)
                {
                    var version = _detailVersion;
                    var run = await service.GetChainRunAsync(openChain.Run.Id);
                    if (version == _detailVersion && run is not null)
                    {
                        OpenChain = openChain with { Run = run };
                        var selected = SelectedChainStepRunId
                            ?? run.Steps.LastOrDefault(step => step.RunId is { } id && id != Guid.Empty)?.RunId;
                        await SelectChainLogAsync(selected?.ToString());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (Tab == tab) Message = ex.Message;
        }
        NotifyStateChanged();
    }

    public async Task ApplyDeepLinkAsync()
    {
        var detailVersion = _detailVersion;
        if (Guid.TryParse(RunId, out var runId) && Open?.Run.Id != runId)
        {
            var report = Reports?.FirstOrDefault(item => item.Run.Id == runId);
            if (report is null)
            {
                try
                {
                    var run = await service.GetJobRunAsync(runId);
                    if (run is not null)
                    {
                        var job = await service.GetJobAsync(run.JobId);
                        var project = await service.GetProjectByIdAsync(run.ProjectId);
                        report = new RunReportView(
                            run.JobId,
                            job?.Name ?? "(deleted job)",
                            project?.Name ?? "(deleted project)",
                            run
                        );
                    }
                }
                catch (Exception ex)
                {
                    Message = ex.Message;
                }
            }

            if (detailVersion != _detailVersion) return;
            if (report is not null)
                await OpenRun(report);
            else
                Message ??= "That job run could not be found.";
        }
        if (Guid.TryParse(ChainRunId, out var chainId) && OpenChain?.Run.Id != chainId)
        {
            var chain = ChainReports?.FirstOrDefault(report => report.Run.Id == chainId);
            if (chain is null)
            {
                try
                {
                    if (await service.GetChainRunAsync(chainId) is { } run)
                    {
                        var project = await service.GetProjectByIdAsync(run.ProjectId);
                        chain = new ChainRunReportView(run.ProjectId, project?.Name ?? "(deleted project)", run);
                    }
                }
                catch (Exception ex) { Message = ex.Message; }
            }
            if (detailVersion != _detailVersion) return;
            if (chain is not null)
            {
                Tab = ChainsTab;
                await OpenChainRun(chain);
            }
            else Message ??= "That workflow run could not be found.";
        }
    }

    private static string NormalizeTab(string? tab) =>
        tab == "chains" ? ChainsTab : tab is ChainsTab or TracesTab or LogsTab ? tab : RunsTab;

    public void SelectTab(string tab)
    {
        Tab = NormalizeTab(tab);
        ClearDetails();
        var uri = DetailFreeUri();
        if (uri != navigation.Uri) navigation.NavigateTo(uri);
    }

    public async Task RestoreTabAsync(string? tab)
    {
        var restored = NormalizeTab(tab);
        if (Tab != restored && RunId is null && ChainRunId is null) ClearDetails();
        Tab = restored;
        if (IsLogsTab && LogResults is null) await QueryLogsAsync();
        if (IsTracesTab) LiveTraces = telemetry.RecentRuns(50);
        if ((IsRunsTab && Reports is null) || (IsChainsTab && ChainReports is null))
            await RefreshAsync();
    }

    public async Task LoadTracesAsync()
    {
        SelectTab(TracesTab);
        LiveTraces = telemetry.RecentRuns(50);
        await Task.CompletedTask;
        NotifyStateChanged();
    }

    public async Task OpenRun(RunReportView report)
    {
        var version = ++_detailVersion;
        try
        {
            if (await service.GetJobRunAsync(report.Run.Id) is { } detail)
                report = report with { Run = detail };
        }
        catch (Exception ex) { if (version == _detailVersion) Message = ex.Message; return; }
        IReadOnlyList<RunArtifactLinkView>? artifacts = null;
        JobRunTelemetry? trace = null;
        IReadOnlyList<TraceSpanNode>? spans = null;
        try { artifacts = await service.ListRunArtifactsAsync(report.Run.Id); }
        catch { }
        try
        {
            trace = (await service.ListJobRunTelemetryAsync(report.JobId)).FirstOrDefault(item => item.RunId == report.Run.Id);
            spans = telemetry.TraceForRun(report.Run.Id);
        }
        catch { }
        // A tab change or close can happen while detail requests are still in flight.
        if (version != _detailVersion) return;
        OpenChain = null;
        Open = report;
        OpenArtifacts = artifacts;
        OpenTelemetry = trace;
        OpenTraceSpans = spans;
        NotifyStateChanged();
    }

    private void ClearDetails()
    {
        CloseLogEvent();
        _detailVersion++;
        RunId = null;
        ChainRunId = null;
        Open = null;
        OpenChain = null;
        SelectedChainStepRunId = null;
        ChainStepRunDetail = null;
        ChainLogsError = null;
        OpenArtifacts = null;
        OpenTelemetry = null;
        OpenTraceSpans = null;
    }

    private string DetailFreeUri() => navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
    {
        ["tab"] = Tab, ["run"] = null, ["workflowRun"] = null, ["chainRun"] = null,
    });

    private void CloseDetails()
    {
        ClearDetails();
        var uri = DetailFreeUri();
        if (uri != navigation.Uri) navigation.NavigateTo(uri, replace: true);
    }

    public void CloseRun() => CloseDetails();

    public async Task OpenLiveTrace(JobRunTelemetry trace)
    {
        ClearDetails();
        OpenTelemetry = trace;
        OpenTraceSpans = telemetry.TraceForRun(trace.RunId);
        RunId = trace.RunId.ToString();
        await ApplyDeepLinkAsync();
    }

    public void CloseLiveTrace() => CloseDetails();

    public async Task OpenChainRun(ChainRunReportView report)
    {
        ClearDetails();
        OpenChain = report;
        var step = report.Run.Steps.LastOrDefault(step => step.RunId is { } id && id != Guid.Empty);
        await SelectChainLogAsync(step?.RunId?.ToString());
    }

    public void CloseChainRun() => CloseDetails();

    public Task OpenChainStep(ChainStepRunView step) => SelectChainLogAsync(step.RunId?.ToString());

    public async Task SelectChainLogAsync(string? value)
    {
        var version = _detailVersion;
        SelectedChainStepRunId = Guid.TryParse(value, out var id)
            && OpenChain?.Run.Steps.Any(step => step.RunId == id) == true ? id : null;
        ChainStepRunDetail = null;
        ChainLogsError = null;
        if (SelectedChainStepRunId is not { } selected) return;
        try
        {
            var detail = await service.GetJobRunAsync(selected);
            if (version != _detailVersion || SelectedChainStepRunId != selected) return;
            ChainStepRunDetail = detail;
            if (detail is null) ChainLogsError = "That job run could not be found.";
        }
        catch (Exception ex)
        {
            if (version == _detailVersion && SelectedChainStepRunId == selected) ChainLogsError = ex.Message;
        }
        NotifyStateChanged();
    }

    public void OpenProject(Guid projectId, string suffix) =>
        navigation.NavigateTo($"/project/{projectId}/{suffix}");

    public void ReplayRun(Guid runId, Guid projectId)
    {
        var jobName = Open?.JobName ?? "job";
        var tenant = PlaceContext.Infrastructure.Tenancy.CurrentTenant.Current;
        if (tenant is null)
        {
            Message = "No tenant resolved — sign in again.";
            return;
        }
        var newRunId = Guid.NewGuid();
        operations.Run(
            tenant,
            projectId,
            $"Replay — {jobName}",
            $"/observability?run={newRunId}",
            async (sp, ct) =>
            {
                var result = await sp.GetRequiredService<PlaceContextService>()
                    .ReplayRunAsync(runId, newRunId, ct);
                return $"replay finished — {result.Status}";
            },
            correlationKey: PlaceContext.Application.Features.RunStatusWatchService.JobRunKey(
                newRunId
            )
        );
        Message =
            $"Replay of {jobName} started in the background — follow it in the notifications bell.";
        CloseRun();
    }

    public static string FormatMilliseconds(double? milliseconds) =>
        milliseconds switch
        {
            null => "—",
            < 1000 => $"{milliseconds:0} ms",
            < 60000 => $"{milliseconds / 1000:0.#} s",
            _ => $"{milliseconds / 60000:0.#} m",
        };

    public static string FormatDuration(DateTimeOffset start, DateTimeOffset end) =>
        ChartPresentation.Duration(start, end);

    public static string FormatBytes(long bytes) =>
        bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0.#} MB"
        : bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB"
        : $"{bytes} B";

    public static string DataUri(RunArtifactView artifact)
    {
        var type = artifact.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? "application/json"
            : "text/plain";
        var content = artifact.IsBinary
            ? artifact.Content
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(artifact.Content));
        return $"data:{type};base64,{content}";
    }

    public static string PrettyJson(string raw)
    {
        try
        {
            return JsonSerializer.Serialize(
                JsonDocument.Parse(raw).RootElement,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
        catch
        {
            return raw;
        }
    }

    public static string StatusColor(string status) =>
        status switch
        {
            "Succeeded" => "var(--good)",
            "Failed" => "var(--bad)",
            "Partial" => "var(--warn)",
            "Running" => "var(--brand-2)",
            _ => "var(--text-3)",
        };

    public static string StatusBackground(string status) =>
        status switch
        {
            "Succeeded" => "var(--good-bg)",
            "Failed" => "var(--bad-bg)",
            "Partial" => "var(--warn-bg)",
            "Running" => "var(--brand-bg)",
            _ => "var(--card-2)",
        };

    private void OnOperationsChanged()
    {
        Interlocked.Increment(ref _operationVersion);
        _ = RefreshAfterOperationAsync();
    }

    private async Task RefreshAfterOperationAsync()
    {
        if (Interlocked.Exchange(ref _operationRefreshPending, 1) != 0) return;
        var version = 0;
        try
        {
            do
            {
                await Task.Delay(250);
                version = Volatile.Read(ref _operationVersion);
                if (_disposed) return;
                await RefreshAsync();
            }
            while (version != Volatile.Read(ref _operationVersion));
        }
        finally
        {
            Volatile.Write(ref _operationRefreshPending, 0);
            if (!_disposed && version != Volatile.Read(ref _operationVersion))
                _ = RefreshAfterOperationAsync();
        }
    }

    public void Dispose()
    {
        _operationSubscription?.Dispose();
        _disposed = true;
        Detach();
        _logEditorCleanup = DestroyLogEditorAsync();
    }
}
