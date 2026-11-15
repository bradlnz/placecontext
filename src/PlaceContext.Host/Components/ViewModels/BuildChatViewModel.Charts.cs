using PlaceContext.Application.Build;
using PlaceContext.Application.Features;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.ProjectData;
using System.Text.Json;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public sealed record DashboardChartResult(ChartSpec? Spec, DateTimeOffset? UpdatedAt, string? Error = null);
    public Dictionary<Guid, DashboardChartResult> DashboardChartResults { get; } = [];
    public bool ChartsRefreshing { get; private set; }
    public Guid? ExpandedDashboardChartId { get; private set; }
    private int _chartVersion;
    public DashboardChart? EditingChart { get; private set; }
    public string ChartSqlDraft { get; private set; } = "";
    public string? ChartQueryError { get; private set; }
    public bool ChartQuerySaving { get; private set; }
    public IReadOnlyList<ProjectTableSchema> ChartQueryTables { get; private set; } = [];

    public async Task EditChartQueryAsync(DashboardChart chart)
    {
        if (Locked || AgentLoading || !Tiles.Charts.Contains(chart)) return;
        EditingChart = chart;
        ChartSqlDraft = chart.Sql ?? "";
        ChartQueryError = null;
        ChartQueryTables = [];
        var projectId = Conversation.ProjectId;
        try
        {
            if (!await permissions.HasAsync(Permission.ProjectsView) || !await permissions.HasAsync(Permission.DataRead))
                throw new UnauthorizedAccessException();
            var tables = await service.ListProjectTableSchemasAsync(projectId);
            if (Conversation.ProjectId == projectId && EditingChart == chart) ChartQueryTables = tables;
        }
        catch (Exception)
        {
            if (Conversation.ProjectId == projectId && EditingChart == chart)
                ChartQueryError = "Could not load the project's table schema. Check data access before saving.";
        }
    }

    public void CloseChartQuery()
    {
        if (!ChartQuerySaving) EditingChart = null;
    }

    public async Task SaveChartQueryAsync(string sql)
    {
        if (Locked || AgentLoading || ChartQuerySaving || EditingChart is not { } chart) return;
        ChartQuerySaving = true;
        ChartQueryError = null;
        ChartSqlDraft = sql;
        var projectId = Conversation.ProjectId;
        var version = _chartVersion;
        try
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("Enter a read-only SELECT query.");
            var definition = ChartSpec.TryParse(chart.ChartJson)!;
            var spec = await DashboardChartData.ReadAsync(service, permissions, projectId,
                definition.Title ?? "Chart", definition.Type, sql);
            if (version != _chartVersion) return;
            var saved = JsonSerializer.Deserialize<DashboardSettings>(JsonSerializer.Serialize(Dashboard))!;
            var index = saved.ForAgent(Conversation.AgentId).Charts.FindIndex(c => c.Id == chart.Id);
            if (index < 0) throw new InvalidOperationException("This chart was removed. Close the editor and reload the dashboard.");
            var updated = chart with { Sql = sql, Live = true,
                Source = "Project data query · up to 24 labels and 3 numeric series (one for pie).",
                ChartJson = spec?.ToJson() ?? chart.ChartJson };
            saved.ForAgent(Conversation.AgentId).Charts[index] = updated;
            await SaveDashboardAsync(saved);
            if (version != _chartVersion) return;
            if (!Tiles.Charts.Contains(updated)) throw new InvalidOperationException(Error ?? "Could not save the chart query.");
            DashboardChartResults[chart.Id] = new(spec, DateTimeOffset.UtcNow);
            if (ExpandedDashboardChartId == chart.Id && spec is not null) PreviewChart = new(spec, updated.Source, true, sql);
            EditingChart = null;
        }
        catch (Exception ex)
        {
            if (version == _chartVersion) ChartQueryError = ex.Message;
        }
        finally
        {
            if (version == _chartVersion)
            {
                ChartQuerySaving = false;
                NotifyStateChanged();
            }
        }
    }

    private void ResetDashboardCharts()
    {
        ++_chartVersion;
        ChartsRefreshing = false;
        DashboardChartResults.Clear();
        EditingChart = null;
        ChartQuerySaving = false;
    }

    public void OpenDashboardChart(DashboardChart chart)
    {
        if (DashboardChartResults.GetValueOrDefault(chart.Id)?.Spec is not { } spec) return;
        OpenChart(new(spec, chart.Source, chart.Live, chart.Sql));
        ExpandedDashboardChartId = chart.Id;
    }

    public async Task RefreshDashboardChartsAsync(CancellationToken ct = default)
    {
        if (ChartsRefreshing || ChartQuerySaving || Tiles.Charts.Count == 0) return;
        ChartsRefreshing = true;
        var version = _chartVersion;
        var projectId = Conversation.ProjectId;
        try
        {
            foreach (var chart in Tiles.Charts.Where(c => !Tiles.HiddenTiles.Contains(c.Id.ToString())).ToArray())
            {
                ct.ThrowIfCancellationRequested();
                DashboardChartResult result;
                try
                {
                    if (!chart.Live) throw new InvalidOperationException("Recreate this chart with a live data source to see current values.");
                    var definition = ChartSpec.TryParse(chart.ChartJson)!;
                    var spec = await DashboardChartData.ReadAsync(service, permissions, projectId,
                        definition.Title ?? "Chart", definition.Type, chart.Sql, ct);
                    result = new(spec, DateTimeOffset.UtcNow);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    result = new(null, null, chart.Live
                        ? "Could not refresh this chart. Check data access and the query; it will retry automatically."
                        : "Recreate this chart with a live data source to see current values.");
                }
                if (version != _chartVersion || ct.IsCancellationRequested) return;
                if (!Tiles.Charts.Contains(chart)) continue;
                DashboardChartResults[chart.Id] = result;
                if (ExpandedDashboardChartId == chart.Id && result.Spec is { } current)
                    PreviewChart = new(current, chart.Source, chart.Live, chart.Sql);
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (version == _chartVersion)
            {
                ChartsRefreshing = false;
                NotifyStateChanged();
            }
        }
    }
}
