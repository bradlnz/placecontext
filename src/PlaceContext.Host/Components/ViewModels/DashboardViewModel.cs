using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DashboardViewModel : PageViewModel, IDisposable
{
    private const string AllFilter = "all";
    private const string RunningFilter = "running";
    private const string FailedFilter = "failed";
    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _js;
    private readonly ICurrentTenant _tenant;
    private readonly OperationCenter _operations;
    private readonly BackgroundOperationRunner _ops;
    private readonly Dictionary<string, DateTimeOffset> _rendered = new();
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private bool _refreshScheduled;

    public DashboardViewModel(
        IPlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation,
        IJSRuntime js,
        ICurrentTenant tenant,
        OperationCenter operations
    )
    {
        _service = service;
        _ui = ui;
        _navigation = navigation;
        _js = js;
        _tenant = tenant;
        _operations = operations;
        _ops = new BackgroundOperationRunner(operations);
    }

    public bool Ready { get; private set; }
    public IReadOnlyList<RunReportView> Reports { get; private set; } =
        Array.Empty<RunReportView>();
    public int Running { get; private set; }
    public int Queued { get; private set; }
    public int Failed24 { get; private set; }
    public int Succeeded24 { get; private set; }
    public string Filter { get; private set; } = AllFilter;
    public IReadOnlyList<JobChainView> Chains { get; private set; } = Array.Empty<JobChainView>();
    public IReadOnlyList<JobView> Jobs { get; private set; } = Array.Empty<JobView>();
    public IReadOnlyList<ProjectChartView> Charts { get; private set; } =
        Array.Empty<ProjectChartView>();
    public IReadOnlyList<DataEntityView> Entities { get; private set; } =
        Array.Empty<DataEntityView>();
    public IReadOnlyList<ProjectTableInfo> EntityTables { get; private set; } =
        Array.Empty<ProjectTableInfo>();
    public Dictionary<Guid, EntityChart> EntityCharts { get; } = new();
    public ParameterPromptState RunPrompt { get; } = new();
    public JobChainView? RunPromptChain { get; private set; }
    public List<(int Index, JobView Job)> RunPromptSteps { get; } = new();
    public Guid? RunningChainId { get; private set; }
    public string? ChainMessage { get; private set; }
    public bool ChainError { get; private set; }
    public string WorkspaceProjectName => _ui.CurrentProjectName ?? "the workspace";
    public Guid? CurrentProjectId => _ui.CurrentProjectId;
    public IReadOnlyList<string> FilterOptions { get; } = ["all", "running", "failed"];

    public bool IsFilter(string filter) => Filter == filter;

    public string ShardSummary(JobRunDetailView run) =>
        $"✓{run.ShardResults.Count(s => s.Outcome == "Succeeded")}"
        + (
            run.ShardResults.Any(s => s.Outcome == "Failed")
                ? $" ✗{run.ShardResults.Count(s => s.Outcome == "Failed")}"
                : ""
        );

    public string ChartDate(DateTimeOffset value) => Presentation.DateTime(value);

    public string StatusTextColor(string status) => Presentation.StatusColor(status);

    public string StatusLabel(string status) => Presentation.UpperStatus(status);

    public bool IsRunningStatus(string status) => Presentation.IsRunning(status);

    public string StartedLabel(DateTimeOffset value) => Presentation.TimeWithMonth(value);

    public sealed record EntityChart(
        string Column,
        IReadOnlyList<(string Label, string Count, int Frac)> Bars
    );

    public void Initialize()
    {
        _operations.Changed += OnOperationsChanged;
        _ui.Set("Dashboard", "jobs · runs · artifacts");
    }

    public async Task LoadAsync()
    {
        try
        {
            Reports = await _service.ListRecentRunReportsAsync(take: 50);
        }
        catch { }
        if (_ui.CurrentProjectId is not { } projectId)
        {
            Ready = true;
            return;
        }

        try
        {
            var chains = _service.ListJobChainsAsync(projectId);
            var jobs = _service.ListJobsAsync(projectId);
            await Task.WhenAll(chains, jobs);
            Chains = await chains;
            Jobs = await jobs;
        }
        catch
        {
            Chains = Array.Empty<JobChainView>();
            Jobs = Array.Empty<JobView>();
        }

        try
        {
            Charts = await _service.ListProjectChartsAsync(projectId);
        }
        catch { }
        try
        {
            Entities = await _service.ListDataEntitiesAsync(projectId);
            if (Entities.Count > 0)
            {
                EntityTables = await _service.ListProjectDataTablesAsync(projectId);
                await LoadEntityChartsAsync();
            }
        }
        catch { }

        var dayAgo = DateTimeOffset.UtcNow.AddHours(-24);
        Running = Reports.Count(report => report.Run.Status == "Running");
        Failed24 = Reports.Count(report =>
            report.Run.Status == "Failed" && report.Run.StartedAt >= dayAgo
        );
        Succeeded24 = Reports.Count(report =>
            report.Run.Status is "Succeeded" or "Partial" && report.Run.StartedAt >= dayAgo
        );
        Queued = _tenant.IsResolved
            ? _operations
                .ListForTenant(_tenant.TenantId)
                .Count(operation => operation.Status == PortalOperationStatus.Queued)
            : 0;
        Ready = true;
        NotifyStateChanged();
    }

    public IReadOnlyList<RunReportView> Filtered() =>
        Filter switch
        {
            RunningFilter => Reports.Where(report => report.Run.Status == "Running").ToList(),
            FailedFilter => Reports.Where(report => report.Run.Status == "Failed").ToList(),
            _ => Reports,
        };

    public void SetFilter(string filter) =>
        Filter = filter is RunningFilter or FailedFilter ? filter : AllFilter;

    public IReadOnlyList<ProjectChartView> SqlCharts() =>
        Charts
            .Where(chart =>
                chart.TableName.StartsWith("sql:", StringComparison.Ordinal)
                && chart.Html.TrimStart().StartsWith('{')
            )
            .ToList();

    public string EntityCount(DataEntityView entity) =>
        EntityTables
            .FirstOrDefault(table =>
                string.Equals(table.Name, entity.TableName, StringComparison.OrdinalIgnoreCase)
            )
            ?.RowEstimate.ToString("N0", CultureInfo.CurrentCulture)
        ?? "—";

    public void OpenRun(Guid runId) => _navigation.NavigateTo($"/observability?run={runId}");

    public async Task PrepareQuickChainRunAsync(JobChainView chain)
    {
        RunningChainId = null;
        var plan = ChainParameterPromptPlan.Build(chain, Jobs ?? Array.Empty<JobView>());
        if (plan.Steps.Count > 0)
        {
            RunPromptChain = chain;
            RunPromptSteps.Clear();
            RunPromptSteps.AddRange(plan.Steps);
            RunPrompt.Reset(plan.Defaults);
            ChainError = false;
            ChainMessage = null;
            NotifyStateChanged();
            return;
        }

        await RunQuickChainCoreAsync(chain, null);
    }

    public async Task SubmitQuickChainPromptAsync()
    {
        if (RunPromptChain is null)
            return;
        if (!RunPrompt.ValidateChainStepParameters(RunPromptSteps))
        {
            ChainError = true;
            ChainMessage = RunPrompt.Error;
            NotifyStateChanged();
            return;
        }

        var overrides = RunPrompt.ToStepPayloadOverrides(RunPromptSteps);
        var payload = overrides.GetValueOrDefault(0);
        await RunQuickChainCoreAsync(RunPromptChain, payload, overrides);
    }

    public void CancelQuickChainPrompt()
    {
        RunPromptChain = null;
        RunPromptSteps.Clear();
        RunPrompt.Clear();
        RunningChainId = null;
        ChainMessage = null;
        ChainError = false;
    }

    private async Task RunQuickChainCoreAsync(
        JobChainView chain,
        string? payload,
        IReadOnlyDictionary<int, string>? stepOverrides = null
    )
    {
        ChainMessage = null;
        ChainError = false;
        RunningChainId = chain.Id;
        NotifyStateChanged();

        var chainRunId = Guid.NewGuid();
        var err = _ops.TryRun(
            chain.ProjectId,
            $"Run chain — {chain.Name}",
            $"/project/{chain.ProjectId}/chains",
            async (sp, ct) =>
            {
                var result = await sp
                    .GetRequiredService<IPlaceContextService>()
                    .RunJobChainAsync(chain.Id, payload, chainRunId, stepOverrides, ct);
                return $"chain finished — {result.Status}";
            },
            correlationKey: RunStatusWatchService.ChainRunKey(chainRunId)
        );

        if (err is not null)
        {
            ChainError = true;
            ChainMessage = err;
            RunningChainId = null;
            NotifyStateChanged();
            return;
        }

        RunPromptChain = null;
        RunPromptSteps.Clear();
        RunPrompt.Clear();
        ChainMessage = $"Run of {chain.Name} started — follow it in the notifications bell.";
        RunningChainId = null;
        NotifyStateChanged();
        await Task.CompletedTask;
    }

    public static string CanvasId(string slot) => ChartPresentation.CanvasId("pcdash-", slot);

    public static string Duration(JobRunDetailView run) =>
        ChartPresentation.Duration(run.StartedAt, run.FinishedAt);

    public static string StatusColor(string status) =>
        status switch
        {
            "Succeeded" or "Running" => "var(--good)",
            "Failed" => "var(--bad)",
            "Partial" => "var(--warn)",
            _ => "var(--text-3)",
        };

    private async Task LoadEntityChartsAsync()
    {
        foreach (var entity in Entities.Take(6))
        {
            if (EntityCharts.ContainsKey(entity.Id))
                continue;
            try
            {
                var columns = await _service.ListProjectTableColumnsAsync(
                    entity.ProjectId,
                    entity.TableName
                );
                var column = entity
                    .Relations.Select(relation => relation.Column)
                    .Concat(
                        columns
                            .Where(column =>
                                (column.Type.Contains("text") || column.Type.Contains("char"))
                                && !string.Equals(
                                    column.Name,
                                    entity.LabelColumn,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            .Select(column => column.Name)
                    )
                    .FirstOrDefault();
                if (column is null)
                    continue;
                var result = await _service.ExecuteProjectDataAsync(
                    entity.ProjectId,
                    $"SELECT \"{column.Replace("\"", "")}\"::text, count(*) FROM \"{entity.TableName.Replace("\"", "")}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 4"
                );
                if (result.Rows.Count == 0)
                    continue;
                var max = result.Rows.Max(row => long.TryParse(row[1], out var count) ? count : 0);
                EntityCharts[entity.Id] = new EntityChart(
                    column,
                    result
                        .Rows.Select(row =>
                            (
                                row[0] ?? "—",
                                row[1] ?? "0",
                                max > 0 && long.TryParse(row[1], out var count)
                                    ? (int)(count * 100 / max)
                                    : 0
                            )
                        )
                        .ToList()
                );
            }
            catch { }
        }
    }

    private void OnOperationsChanged() => _ = RefreshAfterOperationAsync();

    private async Task RefreshAfterOperationAsync()
    {
        if (_refreshScheduled)
            return;
        _refreshScheduled = true;
        try
        {
            var wait = _lastRefresh + TimeSpan.FromSeconds(5) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait);
            _lastRefresh = DateTimeOffset.UtcNow;
            await LoadAsync();
        }
        catch { }
        finally
        {
            _refreshScheduled = false;
        }
    }

    public async Task RenderChartsAsync()
    {
        foreach (var chart in SqlCharts())
        {
            var id = CanvasId(chart.TableName);
            if (_rendered.TryGetValue(id, out var timestamp) && timestamp == chart.GeneratedAt)
                continue;
            try
            {
                await _js.InvokeVoidAsync("pcchart.render", id, chart.Html);
                _rendered[id] = chart.GeneratedAt;
            }
            catch (JSException) { }
        }
    }

    public void Dispose() => _operations.Changed -= OnOperationsChanged;
}
