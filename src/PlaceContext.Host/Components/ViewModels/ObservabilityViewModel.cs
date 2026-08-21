using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ObservabilityViewModel(
    IPlaceContextService service,
    PortalUiState ui,
    NavigationManager navigation,
    OperationCenter operations,
    IJobTelemetryReader telemetry
) : PageViewModel, IDisposable
{
    public const string RunsTab = "runs";
    public const string ChainsTab = "chains";
    public const string TracesTab = "traces";
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
    public IReadOnlyList<(string Path, string Value)> OpenChainContextRows =>
        OpenChain is null ? Array.Empty<(string, string)>() : FlattenContext(OpenChain.Run.FinalOutput);
    public string Tab { get; private set; } = RunsTab;
    public bool IsRunsTab => Tab == RunsTab;
    public bool IsChainsTab => Tab == ChainsTab;
    public bool IsTracesTab => Tab == TracesTab;
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
        operations.Changed += OnOperationsChanged;
        await RefreshAsync();
        ui.Set("Observability", "cross-project job history");
        Loading = false;
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
        FailedShardCount(run) == 0 ? string.Empty : $" ✗ {FailedShardCount(run)}";

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
        Message = null;
        try
        {
            Reports = await service.ListRecentRunReportsAsync(take: 50);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        try
        {
            ChainReports = await service.ListRecentChainRunsAsync(take: 50);
        }
        catch (Exception ex)
        {
            Message ??= ex.Message;
        }
        NotifyStateChanged();
    }

    public async Task ApplyDeepLinkAsync()
    {
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

            if (report is not null)
                await OpenRun(report);
            else
                Message ??= "That job run could not be found.";
        }
        if (
            Guid.TryParse(ChainRunId, out var chainId)
            && OpenChain?.Run.Id != chainId
            && ChainReports?.FirstOrDefault(report => report.Run.Id == chainId) is { } chain
        )
        {
            Tab = ChainsTab;
            OpenChain = chain;
        }
    }

    public void SelectTab(string tab) => Tab = tab is ChainsTab or TracesTab ? tab : RunsTab;

    public async Task LoadTracesAsync()
    {
        Tab = TracesTab;
        LiveTraces = telemetry.RecentRuns(50);
        await Task.CompletedTask;
        NotifyStateChanged();
    }

    public async Task OpenRun(RunReportView report)
    {
        OpenChain = null;
        Open = report;
        OpenArtifacts = null;
        OpenTelemetry = null;
        OpenTraceSpans = null;
        try
        {
            OpenArtifacts = await service.ListRunArtifactsAsync(report.Run.Id);
        }
        catch { }
        try
        {
            OpenTelemetry = (await service.ListJobRunTelemetryAsync(report.JobId)).FirstOrDefault(
                item => item.RunId == report.Run.Id
            );
            OpenTraceSpans = telemetry.TraceForRun(report.Run.Id);
        }
        catch { }
        NotifyStateChanged();
    }

    public void CloseRun()
    {
        Open = null;
        OpenArtifacts = null;
        OpenTelemetry = null;
        OpenTraceSpans = null;
    }

    public void OpenLiveTrace(JobRunTelemetry trace)
    {
        Open = null;
        OpenChain = null;
        OpenTelemetry = trace;
        OpenTraceSpans = telemetry.TraceForRun(trace.RunId);
        if (Reports?.FirstOrDefault(report => report.Run.Id == trace.RunId) is { } report)
            _ = OpenRun(report);
    }

    public void CloseLiveTrace()
    {
        OpenTelemetry = null;
        OpenTraceSpans = null;
    }

    public void OpenChainRun(ChainRunReportView report) => OpenChain = report;

    public void CloseChainRun() => OpenChain = null;

    public void OpenChainStep(ChainStepRunView step)
    {
        if (step.RunId is { } id && id != Guid.Empty)
        {
            OpenChain = null;
            navigation.NavigateTo($"/observability?run={id}");
        }
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
                var result = await sp.GetRequiredService<IPlaceContextService>()
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

    private void OnOperationsChanged() => _ = RefreshAsync();

    public void Dispose() => operations.Changed -= OnOperationsChanged;
}
