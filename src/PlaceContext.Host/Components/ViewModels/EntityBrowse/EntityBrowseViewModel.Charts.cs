using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class EntityBrowseViewModel
{
    // ── Charts ────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<ProjectChartView> Charts { get; private set; } =
        Array.Empty<ProjectChartView>();
    public bool ShowChartEditor { get; private set; }
    public bool ChartBusy { get; private set; }
    public string ChartName { get; set; } = "";
    public string ChartSql { get; set; } = "";
    public string ChartType { get; set; } = "bar";
    public string? ChartError { get; private set; }
    public bool ChartMonaco { get; set; } = true;
    public bool ChartMonacoReady { get; set; }
    public const string ChartEditorId = "pcmonaco-entchart";
    public Dictionary<string, DateTimeOffset> RenderedCharts { get; } = new();

    // ── Charts ────────────────────────────────────────────────────────────────────────────────
    public async Task OpenAnalyticsAsync()
    {
        ViewTab = "analytics";
        RenderedCharts.Clear();
        ChartMonacoReady = false;
        await EnsureDefaultChartsAsync();
        NotifyStateChanged();
    }

    private async Task EnsureDefaultChartsAsync()
    {
        if (_defaultsTried || Entity is null || EntityCharts().Count > 0)
            return;
        _defaultsTried = true;
        try
        {
            var table = Entity.TableName.Replace("\"", "");
            var columns = await _svc.ListProjectTableColumnsAsync(ProjectId, Entity.TableName);
            var categorical = Entity
                .Relations.Select(r => r.Column)
                .Concat(
                    columns
                        .Where(c =>
                            (c.Type.Contains("text") || c.Type.Contains("char"))
                            && !string.Equals(
                                c.Name,
                                Entity.LabelColumn,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Select(c => c.Name)
                )
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var numeric = columns
                .Where(c =>
                    (
                        c.Type.Contains("int")
                        || c.Type.Contains("numeric")
                        || c.Type.Contains("double")
                        || c.Type.Contains("real")
                    ) && !string.Equals(c.Name, "id", StringComparison.OrdinalIgnoreCase)
                )
                .Select(c => c.Name)
                .ToList();

            async Task Seed(string name, string sql, string type)
            {
                try
                {
                    await _svc.SaveSqlChartAsync(ProjectId, $"{Entity.Name} · {name}", sql, type);
                }
                catch { }
            }

            if (categorical.FirstOrDefault() is { } cat)
            {
                await Seed(
                    $"records by {cat}",
                    $"SELECT \"{cat}\"::text, count(*) FROM \"{table}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 10",
                    "bar"
                );
                if (numeric.FirstOrDefault() is { } num)
                    await Seed(
                        $"avg {num} by {cat}",
                        $"SELECT \"{cat}\"::text, round(avg(\"{num}\")::numeric, 1) FROM \"{table}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 10",
                        "bar"
                    );
                if (categorical.Skip(1).FirstOrDefault() is { } cat2)
                    await Seed(
                        $"share by {cat2}",
                        $"SELECT \"{cat2}\"::text, count(*) FROM \"{table}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 8",
                        "pie"
                    );
            }
            else if (numeric.FirstOrDefault() is { } num && LabelCol() is { } label)
            {
                await Seed(
                    $"{num} by {label}",
                    $"SELECT \"{label.Replace("\"", "")}\"::text, \"{num}\" FROM \"{table}\" ORDER BY 2 DESC LIMIT 12",
                    "bar"
                );
            }

            Charts = await _svc.ListProjectChartsAsync(ProjectId);
        }
        catch { }
    }

    public void ToggleChartEditor()
    {
        ShowChartEditor = !ShowChartEditor;
        ChartMonacoReady = false;
        ChartError = null;
        if (ShowChartEditor && string.IsNullOrWhiteSpace(ChartSql) && Entity is not null)
            ChartSql =
                $"SELECT \"{LabelCol()}\", count(*) FROM \"{Entity.TableName}\" GROUP BY 1 ORDER BY 2 DESC LIMIT 10";
        NotifyStateChanged();
    }

    public async Task SaveEntityChartAsync(
        Func<string, string, Task<string>>? getMonacoValue = null
    )
    {
        if (Entity is null)
            return;
        if (ChartMonaco && ChartMonacoReady && getMonacoValue is not null)
        {
            try
            {
                ChartSql = await getMonacoValue(ChartEditorId, ChartSql);
            }
            catch { }
        }
        ChartError = null;
        if (string.IsNullOrWhiteSpace(ChartName))
        {
            ChartError = "Give the chart a name.";
            NotifyStateChanged();
            return;
        }
        ChartBusy = true;
        try
        {
            await _svc.SaveSqlChartAsync(
                ProjectId,
                $"{Entity.Name} · {ChartName.Trim()}",
                ChartSql,
                ChartType
            );
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            ShowChartEditor = false;
            ChartName = "";
        }
        catch (Exception ex)
        {
            ChartError = ex.Message;
        }
        finally
        {
            ChartBusy = false;
            NotifyStateChanged();
        }
    }

    public async Task SwitchChartTypeAsync(ProjectChartView chart, string type)
    {
        if (StoredSql(chart) is not { } sql || StoredType(chart) == type)
            return;
        try
        {
            await _svc.SaveSqlChartAsync(ProjectId, chart.TableName["sql:".Length..], sql, type);
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ChartError = ex.Message;
            NotifyStateChanged();
        }
    }

    public async Task RefreshChartAsync(ProjectChartView chart)
    {
        if (StoredSql(chart) is not { } sql)
            return;
        try
        {
            await _svc.SaveSqlChartAsync(
                ProjectId,
                chart.TableName["sql:".Length..],
                sql,
                StoredType(chart)
            );
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ChartError = ex.Message;
            NotifyStateChanged();
        }
    }

    public async Task DeleteChartAsync(ProjectChartView chart)
    {
        try
        {
            await _svc.DeleteSqlChartAsync(ProjectId, chart.TableName["sql:".Length..]);
            Charts = await _svc.ListProjectChartsAsync(ProjectId);
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            ChartError = ex.Message;
            NotifyStateChanged();
        }
    }
}
