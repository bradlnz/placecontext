using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application;
using PlaceContext.Host.Components.Models;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectAnalyticsViewModel(
    PlaceContextService service,
    PortalUiState ui,
    IJSRuntime js,
    IAnalyticsRefreshQueue queue
) : PageViewModel, IDisposable
{
    public static DataSection ActiveSection => DataSection.Analytics;
    public Guid ProjectId
    {
        get; private set;
    }
    public IReadOnlyList<ProjectTableInfo>? Tables
    {
        get; private set;
    }
    public IReadOnlyList<ProjectChartView> Charts
    {
        get; private set;
    } =
        Array.Empty<ProjectChartView>();
    public string? Error
    {
        get; private set;
    }
    public string? RedrawFor
    {
        get; private set;
    }
    public string Instruction { get; set; } = string.Empty;
    public bool SweepPending
    {
        get; private set;
    }
    public bool ShowSqlEditor
    {
        get; private set;
    }
    public bool SqlBusy
    {
        get; private set;
    }
    public bool SqlMonaco { get; private set; } = true;
    public bool SqlMonacoReady
    {
        get; private set;
    }
    public const string SqlEditorId = "pcmonaco-sqlchart";
    public string SqlName { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public string SqlType { get; set; } = "bar";
    public string? SqlError
    {
        get; private set;
    }
    public bool LiveEnabled { get; set; } = true;
    public bool Refreshing { get; private set; }
    private int _loadVersion;
    private CancellationTokenSource? _loadCancellation;
    private readonly Dictionary<string, string> _rendered = new();
    private bool _sqlSchemaPushed;

    public async Task LoadAsync(Guid projectId)
    {
        if (ProjectId != projectId)
        {
            _sqlSchemaPushed = false;
            ShowSqlEditor = false;
            SqlMonacoReady = false;
            SqlName = string.Empty;
            SqlQuery = string.Empty;
            SqlError = null;
        }
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        ++_loadVersion;
        Refreshing = false;
        ProjectId = projectId;
        Charts = [];
        Tables = null;
        ResetChartCache();
        ui.Set("Analytics", "live charts over the project's data");
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (Refreshing)
        {
            return;
        }
        Refreshing = true;
        var version = _loadVersion;
        var projectId = ProjectId;
        var ct = _loadCancellation?.Token ?? CancellationToken.None;
        SweepPending = queue.IsPending(projectId);
        NotifyStateChanged();
        try
        {
            var tables = await service.ListProjectDataTablesAsync(projectId, ct);
            var charts = await service.ListProjectChartsAsync(projectId, ct, live: true);
            if (version != _loadVersion)
            {
                return;
            }
            Tables = tables;
            Charts = charts;
            Error = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                Error = ex.Message;
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                Refreshing = false;
                NotifyStateChanged();
            }
        }
    }

    public async Task RenderAsync()
    {
        if (ShowSqlEditor && !_sqlSchemaPushed)
        {
            _sqlSchemaPushed = true;
            await SqlSchemaHelper.PushAsync(service, js, ProjectId, includeIndexes: true);
        }
        if (ShowSqlEditor && SqlMonaco && !SqlMonacoReady)
        {
            SqlMonacoReady = true;
            try
            {
                SqlMonaco = await js.InvokeAsync<bool>(
                    "pcmonaco.init",
                    SqlEditorId,
                    SqlQuery,
                    "sql",
                    "vs-dark"
                );
            }
            catch
            {
                SqlMonaco = false;
            }
        }
        var entries = new List<object>();
        var rendered = new List<(string Id, string Html)>();
        foreach (var chart in Charts.Where(IsSpec))
        {
            var id = CanvasId(chart.TableName);
            if (_rendered.TryGetValue(id, out var html) && html == chart.Html)
            {
                continue;
            }
            entries.Add(new
            {
                id,
                spec = chart.Html
            });
            rendered.Add((id, chart.Html));
        }
        if (entries.Count > 0)
        {
            try
            {
                await js.InvokeVoidAsync("pcchart.renderAll", entries);
                foreach (var (id, at) in rendered)
                {
                    _rendered[id] = at;
                }
            }
            catch (JSException) { }
        }
    }

    // The view model is circuit-scoped and outlives the page, but the canvases it renders into are
    // created per page mount. Clear the rendered-once cache whenever the page (re)mounts so charts
    // redraw the fresh canvases instead of being skipped as "already drawn".
    public void ResetChartCache() => _rendered.Clear();

    public IReadOnlyList<ProjectChartView> SqlCharts() =>
        Charts
            .Where(chart => chart.TableName.StartsWith("sql:", StringComparison.Ordinal))
            .ToList();

    public ProjectChartView? ChartFor(string table) =>
        Charts.FirstOrDefault(chart =>
            string.Equals(chart.TableName, table, StringComparison.OrdinalIgnoreCase)
        );

    public static bool IsSpec(ProjectChartView chart) => chart.Html.TrimStart().StartsWith('{');

    public static string CanvasId(string table) => ChartPresentation.CanvasId("pcchart-", table);

    public void GenerateAll()
    {
        Enqueue();
    }

    public bool IsPending(string? table = null) => queue.IsPending(ProjectId, table);

    public Task RedrawAsync(string table)
    {
        Enqueue(table);
        RedrawFor = null;
        Instruction = string.Empty;
        return Task.CompletedTask;
    }

    public async Task SaveSqlAsync()
    {
        if (SqlBusy)
        {
            return;
        }
        SqlBusy = true;
        SqlError = null;
        try
        {
            if (SqlMonaco && SqlMonacoReady)
            {
                SqlQuery = await js.InvokeAsync<string>("pcmonaco.getValue", SqlEditorId);
            }
            await service.SaveSqlChartAsync(ProjectId, SqlName, SqlQuery, SqlType);
            await ReloadAsync();
            ShowSqlEditor = false;
        }
        catch (Exception ex)
        {
            SqlError = ex.Message;
        }
        finally
        {
            SqlBusy = false;
        }
    }

    public async Task EditSql(ProjectChartView chart)
    {
        ShowSqlEditor = true;
        SqlMonacoReady = false;
        SqlName = chart.TableName["sql:".Length..];
        SqlQuery = StoredSql(chart) ?? string.Empty;
        SqlType = StoredType(chart);
        await Task.CompletedTask;
    }

    public async Task SwitchTypeAsync(ProjectChartView chart, string type)
    {
        if (StoredSql(chart) is { } sql && StoredType(chart) != type)
        {
            await service.SaveSqlChartAsync(ProjectId, chart.TableName["sql:".Length..], sql, type);
            await ReloadAsync();
        }
    }

    public Task RefreshSqlAsync(ProjectChartView chart) => ReloadAsync();

    public async Task DeleteSqlAsync(ProjectChartView chart)
    {
        await service.DeleteSqlChartAsync(ProjectId, chart.TableName["sql:".Length..]);
        await ReloadAsync();
    }

    public void ToggleRedraw(string table)
    {
        RedrawFor = RedrawFor == table ? null : table;
        Instruction = string.Empty;
    }

    public void ToggleSqlEditor()
    {
        ShowSqlEditor = !ShowSqlEditor;
        SqlMonacoReady = false;
        SqlError = null;
    }

    public static string StoredType(ProjectChartView chart) => Read(chart, "type") ?? "bar";

    public string ChartTypeStyle(ProjectChartView chart, string type) =>
        StoredType(chart) == type
            ? "color:var(--brand-2); border-color:var(--brand-2)"
            : string.Empty;

    public static string? StoredSql(ProjectChartView chart) => Read(chart, "sql");

    private static string? Read(ProjectChartView chart, string property)
    {
        try
        {
            return JsonNode.Parse(chart.Html)?[property]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private void Enqueue(string? table = null)
    {
        if (PlaceContext.Application.Tenancy.CurrentTenant.Current is { } tenant)
        {
            queue.TryEnqueue(tenant, ProjectId, tableName: table, instruction: Instruction);
            SweepPending = true;
            return;
        }
        Error = "No tenant resolved — sign in again.";
    }

    public void CancelRedraw() => RedrawFor = null;

    public void Dispose()
    {
        ++_loadVersion;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        Refreshing = false;
        Detach();
    }
}
