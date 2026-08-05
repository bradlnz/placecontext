using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class EntityBrowseViewModel : PageViewModel
{
    public enum BrowseTab
    {
        Records,
        Graph,
        Analytics,
    }

    public IReadOnlyList<string> ChartTypes { get; } = ["bar", "line", "pie"];
    public bool IsRecordsTab => ViewTab == "records";
    public bool IsGraphTab => ViewTab == "graph";
    public bool IsAnalyticsTab => ViewTab == "analytics";

    public void SelectRecords() => ViewTab = "records";

    public void SelectGraph() => ViewTab = "graph";

    public bool IsChartInput(string type) => type == "checkbox";

    public string FormInputType(string type) =>
        type switch
        {
            "integer" or "bigint" or "numeric" or "double" or "real" => "number",
            "boolean" => "checkbox",
            "date" => "date",
            "timestamptz" => "datetime-local",
            _ => "text",
        };

    public bool FormValueIsTrue(string column) =>
        FormValues.GetValueOrDefault(column) is "true" or "1";

    public string CellColor(string column) =>
        column == LabelCol() ? "var(--text)" : "var(--text-2)";

    public string CellWeight(string column) => column == LabelCol() ? "600" : "400";

    public string ArtifactDate(DateTimeOffset value) => Presentation.ShortDateTime(value);

    private readonly IPlaceContextService _svc;
    private readonly IJSRuntime _js;

    public EntityBrowseViewModel(IPlaceContextService svc, IJSRuntime js)
    {
        _svc = svc;
        _js = js;
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }
    public string EntityName { get; set; } = "";
    public string? RecordKey { get; set; }

    // ── Computed ──────────────────────────────────────────────────────────────────────────────
    public string ChartPrefix => $"sql:{Entity?.Name} · ";
    public string ChartEditorIdentifier => ChartEditorId;

    public string ChartCanvasIdentifier(string slot) => ChartCanvasId(slot);

    private bool _defaultsTried;

    public IReadOnlyList<ProjectChartView> EntityCharts() =>
        Charts
            .Where(c => c.TableName.StartsWith(ChartPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public string ChartShortName(ProjectChartView c) => c.TableName[ChartPrefix.Length..];

    public static string ChartCanvasId(string slot) =>
        "pcentchart-"
        + Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(slot))
        )[..12];

    public static string? StoredSql(ProjectChartView chart)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(chart.Html)?["sql"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public static string StoredType(ProjectChartView chart)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(chart.Html)?["type"]?.GetValue<string>()
                ?? "bar";
        }
        catch
        {
            return "bar";
        }
    }

    public static int TotalPages(ProjectTablePageResult page) =>
        Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)Math.Max(1, page.PageSize)));

    public string ChartTypeStyle(ProjectChartView chart, string type) =>
        StoredType(chart) == type
            ? "color:var(--brand-2); border-color:var(--brand-2)"
            : string.Empty;

    public string PageLabel(ProjectTablePageResult page) =>
        $"Page {page.Page} of {TotalPages(page)}";

    public string? NodeUrl(GraphNodeView node) => NodeUrls.GetValueOrDefault(node.Id);

    public static bool IsNumeric(string type) =>
        type.Contains("int")
        || type.Contains("numeric")
        || type.Contains("double")
        || type.Contains("real");

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        NodeUrls.Clear();
    }

    public string EntityNameForTable(string tableName) =>
        AllEntities
            .FirstOrDefault(entity =>
                string.Equals(entity.TableName, tableName, StringComparison.OrdinalIgnoreCase)
            )
            ?.Name
        ?? tableName;

    public async Task AfterRenderAsync()
    {
        if (ViewTab != "analytics")
            return;
        if (ShowChartEditor && ChartMonaco && !ChartMonacoReady)
        {
            ChartMonacoReady = true;
            try
            {
                if (
                    !await _js.InvokeAsync<bool>(
                        "pcmonaco.init",
                        ChartEditorId,
                        ChartSql,
                        "sql",
                        "vs-dark"
                    )
                )
                    ChartMonaco = false;
            }
            catch
            {
                ChartMonaco = false;
            }
        }

        var entries = new List<object>();
        var rendered = new List<(string Id, DateTimeOffset At)>();
        foreach (var chart in EntityCharts().Where(c => c.Html.TrimStart().StartsWith('{')))
        {
            var id = ChartCanvasId(chart.TableName);
            if (RenderedCharts.TryGetValue(id, out var at) && at == chart.GeneratedAt)
                continue;
            entries.Add(new { id, spec = chart.Html });
            rendered.Add((id, chart.GeneratedAt));
        }
        if (entries.Count > 0)
        {
            try
            {
                await _js.InvokeVoidAsync("pcchart.renderAll", entries);
                foreach (var (id, at) in rendered)
                    RenderedCharts[id] = at;
            }
            catch (JSException) { }
        }
    }

    // The view model is circuit-scoped and outlives the page, but the canvases it renders into are
    // created per page mount. Clear the rendered-once cache whenever the page (re)mounts so charts
    // redraw the fresh canvases instead of being skipped as "already drawn".
    public void ResetChartCache() => RenderedCharts.Clear();

    public async Task<string> GetChartSqlAsync(string id, string current)
    {
        try
        {
            return await _js.InvokeAsync<string>("pcmonaco.getValue", id);
        }
        catch
        {
            return current;
        }
    }

    public async Task ClearFocusAsync()
    {
        FocusKey = null;
        await BuildSectionGraphAsync();
    }

    public void ToggleBool(string columnName, bool value) =>
        FormValues[columnName] = value ? "true" : "false";

    public Task OnSearchInputAsync(ChangeEventArgs e) =>
        OnSearchInputAsync(e.Value as string ?? string.Empty);

    public void SetFormValue(string columnName, ChangeEventArgs e) =>
        FormValues[columnName] = e.Value as string;

    public async Task LoadAsync()
    {
        Error = null;
        Open = null;
        Loaded = false;
        try
        {
            AllEntities = await _svc.ListDataEntitiesAsync(ProjectId);
            Entity = AllEntities.FirstOrDefault(e =>
                string.Equals(e.Name, EntityName, StringComparison.OrdinalIgnoreCase)
            );
            if (Entity is null)
                return;
            Rows = await _svc.ExecuteProjectDataAsync(
                ProjectId,
                $"SELECT * FROM \"{Entity.TableName}\" LIMIT 500"
            );
            try
            {
                Charts = await _svc.ListProjectChartsAsync(ProjectId);
            }
            catch { }

            FocusKey = null;
            if (
                RecordKey is { Length: > 0 } recordKey
                && Rows is not null
                && Rows.Rows.Any(r =>
                    r.Any(v => string.Equals(v, recordKey, StringComparison.OrdinalIgnoreCase))
                )
            )
            {
                FocusKey = recordKey;
                ViewTab = "graph";
            }
            await BuildInsightsAsync();
            await BuildSectionGraphAsync();

            Search = "";
            PageNum = 1;
            await LoadRecordsPageAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loaded = true;
            NotifyStateChanged();
        }
    }
}
