using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class EntityBrowseViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;

    public EntityBrowseViewModel(IPlaceContextService svc) => _svc = svc;

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }
    public string EntityName { get; set; } = "";
    public string? RecordKey { get; set; }

    // ── Computed ──────────────────────────────────────────────────────────────────────────────
    public string ChartPrefix => $"sql:{Entity?.Name} · ";
    private bool _defaultsTried;

    public IReadOnlyList<ProjectChartView> EntityCharts()
        => Charts.Where(c => c.TableName.StartsWith(ChartPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

    public string ChartShortName(ProjectChartView c) => c.TableName[ChartPrefix.Length..];

    public static string ChartCanvasId(string slot)
        => "pcentchart-" + Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
               System.Text.Encoding.UTF8.GetBytes(slot)))[..12];

    public static string? StoredSql(ProjectChartView chart)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(chart.Html)?["sql"]?.GetValue<string>(); }
        catch { return null; }
    }

    public static string StoredType(ProjectChartView chart)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(chart.Html)?["type"]?.GetValue<string>() ?? "bar"; }
        catch { return "bar"; }
    }

    public static int TotalPages(ProjectTablePageResult page)
        => Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)Math.Max(1, page.PageSize)));

    public string? NodeUrl(GraphNodeView node) => NodeUrls.GetValueOrDefault(node.Id);

    public static bool IsNumeric(string type) =>
        type.Contains("int") || type.Contains("numeric") || type.Contains("double") || type.Contains("real");

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        ProjectId = projectId;
        NodeUrls.Clear();
    }

    public async Task LoadAsync()
    {
        Error = null;
        Open = null;
        Loaded = false;
        try
        {
            AllEntities = await _svc.ListDataEntitiesAsync(ProjectId);
            Entity = AllEntities.FirstOrDefault(e => string.Equals(e.Name, EntityName, StringComparison.OrdinalIgnoreCase));
            if (Entity is null) return;
            Rows = await _svc.ExecuteProjectDataAsync(ProjectId, $"SELECT * FROM \"{Entity.TableName}\" LIMIT 500");
            try { Charts = await _svc.ListProjectChartsAsync(ProjectId); } catch { }

            FocusKey = null;
            if (RecordKey is { Length: > 0 } recordKey
                && Rows is not null
                && Rows.Rows.Any(r => r.Any(v => string.Equals(v, recordKey, StringComparison.OrdinalIgnoreCase))))
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
        catch (Exception ex) { Error = ex.Message; }
        finally { Loaded = true; NotifyStateChanged(); }
    }

}
