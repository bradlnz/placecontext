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

public sealed class DashboardViewModel : PageViewModel, IComponentViewModel, IDisposable
{
    private const string AllFilter = "all";
    private const string RunningFilter = "running";
    private const string FailedFilter = "failed";
    private readonly PlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _js;
    private readonly ICurrentTenant _tenant;
    private readonly OperationCenter _operations;
    private readonly BackgroundOperationRunner _ops;
    private readonly Dictionary<string, DateTimeOffset> _rendered = new();
    private Guid? _projectId;
    private Guid? _projectOverride;
    private readonly IPermissionService? _permissions;
    private int _loadVersion;

    public DashboardViewModel(
        PlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation,
        IJSRuntime js,
        ICurrentTenant tenant,
        OperationCenter operations,
        IPermissionService? permissions = null
    )
    {
        _service = service;
        _ui = ui;
        _navigation = navigation;
        _js = js;
        _tenant = tenant;
        _operations = operations;
        _permissions = permissions;
        _ops = new BackgroundOperationRunner(operations);
    }

    public bool CanViewActivity { get; private set; }
    public bool Ready { get; private set; }
    public string? LoadError { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public bool ChainsReady { get; private set; }
    public IReadOnlyList<RunReportView> Reports { get; private set; } =
        Array.Empty<RunReportView>();
    public int Running { get; private set; }
    public int Queued { get; private set; }
    public int Failed24 { get; private set; }
    public int Succeeded24 { get; private set; }
    public string Filter { get; private set; } = AllFilter;
    public IReadOnlyList<JobChainView> Chains { get; private set; } = Array.Empty<JobChainView>();
    public IReadOnlyList<ProjectChartView> Charts { get; private set; } =
        Array.Empty<ProjectChartView>();
    public IReadOnlyList<DataEntityView> Entities { get; private set; } =
        Array.Empty<DataEntityView>();
    public IReadOnlyList<ProjectTableInfo> EntityTables { get; private set; } =
        Array.Empty<ProjectTableInfo>();
    public Dictionary<Guid, EntityChart> EntityCharts { get; } = new();
    public bool EntityChartsLoading { get; private set; }
    public string? EntityChartsError { get; private set; }
    public ParameterPromptState RunPrompt { get; } = new();
    public JobChainView? RunPromptChain { get; private set; }
    public List<(int Index, JobView Job)> RunPromptSteps { get; } = new();
    public string RunAttachment { get; private set; } = "";
    public Guid? RunningChainId { get; private set; }
    public string? ChainMessage { get; private set; }
    public bool ChainError { get; private set; }
    public string WorkspaceProjectName => _ui.CurrentProjectName ?? "the workspace";
    public Guid? CurrentProjectId => _projectOverride ?? _ui.CurrentProjectId;
    public IReadOnlyList<string> FilterOptions { get; } = ["all", "running", "queued", "failed", "succeeded"];

    public bool IsFilter(string filter) => Filter == filter;

    public string ShardSummary(JobRunDetailView run) =>
        $"{run.ShardResults.Count(s => s.Outcome == "Succeeded")} succeeded"
        + (
            run.ShardResults.Any(s => s.Outcome == "Failed")
                ? $" · {run.ShardResults.Count(s => s.Outcome == "Failed")} failed"
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

    public void Initialize(Guid? projectId = null)
    {
        _projectOverride = projectId;
        _ui.OnChanged -= OnUiChanged;
        _projectId = CurrentProjectId;
        _ui.OnChanged += OnUiChanged;
        _ui.Set("Dashboard", "");
    }

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        Ready = false; CanViewActivity = false;
        ChainsReady = false;
        Reports = []; Charts = []; Chains = []; Entities = []; EntityTables = [];
        EntityCharts.Clear(); ResetChartCache();
        CapturedAt = null; LoadError = null;
        var reports = LoadReportsAsync(version);
        if (CurrentProjectId is { } projectId)
            await Task.WhenAll(reports, LoadChainsAsync(projectId, version),
                LoadChartsAsync(projectId, version), LoadEntitiesAsync(projectId, version));
        else
            await reports;
    }

    private async Task LoadReportsAsync(int version)
    {
        try
        {
            var allowed = _permissions is null || await _permissions.HasAsync(Permission.JobsView);
            if (version != _loadVersion) return;
            CanViewActivity = allowed;
            if (!allowed)
                throw new UnauthorizedAccessException("Job activity is not available with your permissions.");
            var projectId = CurrentProjectId;
            var reports = await _service.ListRecentRunReportsAsync(take: 50, projectId: projectId);
            var counts = projectId is { } id ? await _service.GetProjectRunCountsAsync(id) : null;
            if (version != _loadVersion) return;
            Reports = reports;
            LoadError = null;
            CapturedAt = DateTimeOffset.UtcNow;
            if (counts is not null)
            {
                Running = counts.GetValueOrDefault("Running");
                Queued = counts.GetValueOrDefault("Queued");
                Failed24 = counts.GetValueOrDefault("Failed");
                Succeeded24 = counts.GetValueOrDefault("Succeeded");
            }
            else UpdateRunMetrics();
        }
        catch
        {
            if (version != _loadVersion) return;
            Reports = []; CapturedAt = null;
            Running = Queued = Failed24 = Succeeded24 = 0;
            LoadError = "Could not load job activity. Please try again.";
        }
        if (version != _loadVersion) return;
        Ready = true;
        NotifyStateChanged();
    }

    private async Task LoadChainsAsync(Guid projectId, int version)
    {
        try
        {
            if (_permissions is not null && !await _permissions.HasAsync(Permission.ChainsManage)) { Chains = []; return; }
            var chains = await _service.ListJobChainsAsync(projectId);
            if (version != _loadVersion) return;
            Chains = chains;
        }
        catch
        {
            if (version != _loadVersion) return;
            Chains = Array.Empty<JobChainView>();
        }
        finally
        {
            if (version == _loadVersion)
            {
                ChainsReady = true;
                NotifyStateChanged();
            }
        }
    }

    private async Task LoadChartsAsync(Guid projectId, int version)
    {
        try
        {
            if (_permissions is not null && !await _permissions.HasAsync(Permission.DataRead)) { Charts = []; return; }
            var charts = await _service.ListProjectChartsAsync(projectId);
            if (version != _loadVersion) return;
            Charts = charts;
            NotifyStateChanged();
        }
        catch { }
    }

    private async Task LoadEntitiesAsync(Guid projectId, int version)
    {
        try
        {
            if (_permissions is not null && !await _permissions.HasAsync(Permission.DataRead)) { Entities = []; return; }
            var entities = await _service.ListDataEntitiesAsync(projectId);
            if (version != _loadVersion) return;
            Entities = entities;
            if (Entities.Count > 0)
            {
                var tables = await _service.ListProjectDataTablesAsync(projectId);
                if (version != _loadVersion) return;
                EntityTables = tables;
            }
            NotifyStateChanged();
        }
        catch { }
    }

    private void UpdateRunMetrics()
    {
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
    }

    public IReadOnlyList<RunReportView> Filtered() =>
        Filter switch
        {
            RunningFilter => Reports.Where(report => report.Run.Status == "Running").ToList(),
            "queued" => Reports.Where(report => report.Run.Status == "Queued").ToList(),
            "succeeded" => Reports.Where(report => report.Run.Status == "Succeeded" && report.Run.StartedAt >= DateTimeOffset.UtcNow.AddHours(-24)).ToList(),
            FailedFilter => Reports.Where(report => report.Run.Status == "Failed" && report.Run.StartedAt >= DateTimeOffset.UtcNow.AddHours(-24)).ToList(),
            _ => Reports,
        };

    public Task RefreshActivityAsync() => LoadReportsAsync(_loadVersion);

    public void SetFilter(string filter) =>
        Filter = filter is RunningFilter or FailedFilter or "queued" or "succeeded" ? filter : AllFilter;

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
        if (RunningChainId is not null || chain.ProjectId != CurrentProjectId) return;
        var version = _loadVersion;
        RunningChainId = chain.Id;
        ChainError = false;
        ChainMessage = null;
        NotifyStateChanged();
        try
        {
            var jobs = await _service.ListJobsAsync(chain.ProjectId);
            if (version != _loadVersion) return;
            var plan = ChainParameterPromptPlan.Build(chain, jobs);
            RunPromptChain = chain;
            RunPromptSteps.Clear();
            RunPromptSteps.AddRange(plan.Steps);
            RunPrompt.Reset(plan.Defaults);
            RunAttachment = "";
        }
        catch (Exception ex)
        {
            if (version != _loadVersion) return;
            ChainError = true;
            ChainMessage = ex.Message;
        }
        finally
        {
            if (version == _loadVersion)
            {
                RunningChainId = null;
                NotifyStateChanged();
            }
        }
    }

    public void SetRunAttachment(string value) => RunAttachment = value;

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
        var payload = ParameterPromptState.WithChainAttachment(
            overrides.GetValueOrDefault(0),
            RunAttachment
        );
        await RunQuickChainCoreAsync(RunPromptChain, payload, overrides);
    }

    public void CancelQuickChainPrompt()
    {
        RunPromptChain = null;
        RunPromptSteps.Clear();
        RunPrompt.Clear();
        RunAttachment = "";
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
            $"Run workflow — {chain.Name}",
            $"/observability?tab=workflows&workflowRun={chainRunId}",
            async (sp, ct) =>
            {
                var result = await sp
                    .GetRequiredService<PlaceContextService>()
                    .RunJobChainAsync(chain.Id, payload, chainRunId, stepOverrides, ct);
                return $"workflow finished — {result.Status}";
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
        RunAttachment = "";
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

    public async Task LoadEntityChartsAsync()
    {
        if (EntityChartsLoading) return;
        EntityChartsLoading = true;
        EntityChartsError = null;
        NotifyStateChanged();
        try { await BuildEntityChartsAsync(); }
        finally
        {
            EntityChartsLoading = false;
            NotifyStateChanged();
        }
    }

    private async Task BuildEntityChartsAsync()
    {
        foreach (var entity in Entities.Take(6))
        {
            if (entity.ProjectId != CurrentProjectId) return;
            if (EntityCharts.ContainsKey(entity.Id))
                continue;
            try
            {
                var columns = await _service.ListProjectTableColumnsAsync(
                    entity.ProjectId,
                    entity.TableName
                );
                if (entity.ProjectId != CurrentProjectId) return;
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
                if (entity.ProjectId != CurrentProjectId) return;
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
            catch (Exception ex)
            {
                if (entity.ProjectId == CurrentProjectId) EntityChartsError = ex.Message;
            }
        }
    }

    private void OnUiChanged()
    {
        if (_projectId == CurrentProjectId) return;
        _projectId = CurrentProjectId;
        Chains = [];
        CancelQuickChainPrompt();
        Charts = [];
        Entities = [];
        EntityTables = [];
        EntityCharts.Clear();
        EntityChartsError = null;
        ResetChartCache();
        _ = LoadAsync();
        NotifyStateChanged();
    }

    public async Task RenderChartsAsync()
    {
        var entries = new List<object>();
        var rendered = new List<(string Id, DateTimeOffset At)>();
        foreach (var chart in SqlCharts())
        {
            var id = CanvasId(chart.TableName);
            if (_rendered.TryGetValue(id, out var timestamp) && timestamp == chart.GeneratedAt)
                continue;
            entries.Add(new { id, spec = chart.Html });
            rendered.Add((id, chart.GeneratedAt));
        }
        if (entries.Count == 0)
            return;
        try
        {
            await _js.InvokeVoidAsync("pcchart.renderAll", entries);
            foreach (var (id, at) in rendered)
                _rendered[id] = at;
        }
        catch (JSException) { }
    }

    // The view model is circuit-scoped and outlives the page, but the canvases it renders into are
    // created per page mount. Clear the rendered-once cache whenever the Dashboard component mounts
    // so returning to the page redraws the fresh canvases instead of skipping them as "already drawn".
    public void ResetChartCache() => _rendered.Clear();

    public void Dispose()
    {
        _ui.OnChanged -= OnUiChanged;
        ++_loadVersion;
        CancelQuickChainPrompt();
        Detach();
    }
}
