using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.Models;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Scheduling;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectAnalyticsViewModel(
    IPlaceContextService service,
    PortalUiState ui,
    IJSRuntime js,
    AnalyticsRefreshQueue queue
) : PageViewModel, IDisposable
{
    public static DataSection ActiveSection => DataSection.Analytics;
    public Guid ProjectId { get; private set; }
    public IReadOnlyList<ProjectTableInfo>? Tables { get; private set; }
    public IReadOnlyList<ProjectChartView> Charts { get; private set; } =
        Array.Empty<ProjectChartView>();
    public string? Error { get; private set; }
    public string? RedrawFor { get; private set; }
    public string Instruction { get; set; } = string.Empty;
    public bool SweepPending { get; private set; }
    public bool ShowSqlEditor { get; private set; }
    public bool SqlBusy { get; private set; }
    public bool SqlMonaco { get; private set; } = true;
    public bool SqlMonacoReady { get; private set; }
    public const string SqlEditorId = "pcmonaco-sqlchart";
    public string SqlName { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public string SqlType { get; set; } = "bar";
    public string? SqlError { get; private set; }
    private System.Threading.Timer? _poll;
    private readonly Dictionary<string, DateTimeOffset> _rendered = new();
    private bool _sqlSchemaPushed;

    public async Task LoadAsync(Guid projectId)
    {
        ProjectId = projectId;
        ui.Set("Analytics", "charts over the project's data");
        await ReloadAsync();
        _poll ??= new System.Threading.Timer(
            _ => _ = PollAsync(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5)
        );
    }

    public async Task ReloadAsync()
    {
        SweepPending = queue.IsPending(ProjectId);
        try
        {
            Tables = await service.ListProjectDataTablesAsync(ProjectId);
            Charts = await service.ListProjectChartsAsync(ProjectId);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        NotifyStateChanged();
    }

    public async Task RenderAsync()
    {
        if (!_sqlSchemaPushed)
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
        var rendered = new List<(string Id, DateTimeOffset At)>();
        foreach (var chart in Charts.Where(IsSpec))
        {
            var id = CanvasId(chart.TableName);
            if (_rendered.TryGetValue(id, out var timestamp) && timestamp == chart.GeneratedAt)
                continue;
            entries.Add(new { id, spec = chart.Html });
            rendered.Add((id, chart.GeneratedAt));
        }
        if (entries.Count > 0)
        {
            try
            {
                await js.InvokeVoidAsync("pcchart.renderAll", entries);
                foreach (var (id, at) in rendered)
                    _rendered[id] = at;
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
        SqlBusy = true;
        SqlError = null;
        try
        {
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

    public async Task RefreshSqlAsync(ProjectChartView chart)
    {
        if (StoredSql(chart) is { } sql)
        {
            await service.SaveSqlChartAsync(
                ProjectId,
                chart.TableName["sql:".Length..],
                sql,
                StoredType(chart)
            );
            await ReloadAsync();
        }
    }

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
        if (PlaceContext.Infrastructure.Tenancy.CurrentTenant.Current is { } tenant)
        {
            queue.TryEnqueue(tenant, ProjectId, tableName: table, instruction: Instruction);
            SweepPending = true;
        }
        else
        {
            Error = "No tenant resolved — sign in again.";
        }
    }

    public void CancelRedraw() => RedrawFor = null;

    private async Task PollAsync()
    {
        var wasPending = SweepPending;
        SweepPending = queue.IsPending(ProjectId);
        if (SweepPending || wasPending)
            await ReloadAsync();
    }

    public void Dispose() => _poll?.Dispose();
}
