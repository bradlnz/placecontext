using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ListProjectChartsHandler(
    IProjectRepository projects,
    IProjectChartRepository charts,
    IProjectDataReader reader,
    IClock clock) : IQueryHandler<ListProjectChartsQuery, IReadOnlyList<ProjectChartView>>
{
    public async Task<IReadOnlyList<ProjectChartView>> HandleAsync(ListProjectChartsQuery q, CancellationToken ct = default)
    {
        await ProjectDataGuard.EnsureExistsAsync(projects, q.ProjectId, ct);
        var result = new List<ProjectChartView>();
        foreach (var chart in await charts.ListForProjectAsync(q.ProjectId, ct))
        {
            var view = new ProjectChartView(chart.TableName, chart.Html, chart.GeneratedAt);
            if (!q.Live)
            {
                result.Add(view);
                continue;
            }
            ct.ThrowIfCancellationRequested();
            try
            {
                var isSql = chart.TableName.StartsWith(SaveSqlChartHandler.Prefix, StringComparison.Ordinal);
                var definition = chart.Html.TrimStart().StartsWith('{') ? JsonNode.Parse(chart.Html) : null;
                var sql = isSql ? definition?["sql"]?.GetValue<string>()
                    : $"SELECT * FROM \"{chart.TableName.Replace("\"", "\"\"")}\" LIMIT 200";
                if (string.IsNullOrWhiteSpace(sql))
                {
                    result.Add(view with { Error = "Saved snapshot: edit this chart and add a SQL query to enable live updates." });
                    continue;
                }
                var data = await reader.QueryAsync(q.ProjectId, sql, ct);
                var name = isSql ? chart.TableName[SaveSqlChartHandler.Prefix.Length..] : chart.TableName;
                var type = definition?["type"]?.GetValue<string>() ?? "bar";
                var html = SqlChartDefinition.Build(name, sql, type, data);
                result.Add(view with { Html = html, GeneratedAt = clock.UtcNow, IsLive = true });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Add(view with { Error = "Refresh failed: " + ex.Message });
            }
        }
        return result;
    }
}
