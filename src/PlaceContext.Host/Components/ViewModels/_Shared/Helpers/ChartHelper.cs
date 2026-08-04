using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Shared chart/SQL-chart helpers used across Dashboard, ProjectAnalytics, and EntityBrowse.
/// Eliminates the CanvasId / StoredSql / StoredType duplication.
/// </summary>
public static class ChartHelper
{
    /// <summary>Stable, DOM-safe canvas id from an arbitrary slot string (MD5 truncated to 12 hex chars).</summary>
    public static string CanvasId(string slot, string prefix = "pcchart-") =>
        prefix
        + Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(slot))
        )[..12];

    /// <summary>Extracts the SQL query from a stored SQL chart's JSON spec.</summary>
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

    /// <summary>Extracts the chart type (bar/line/pie) from a stored SQL chart's JSON spec.</summary>
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

    /// <summary>Filters a chart list to only SQL-defined charts with valid JSON specs.</summary>
    public static IReadOnlyList<ProjectChartView> SqlCharts(
        IReadOnlyList<ProjectChartView> charts
    ) =>
        charts
            .Where(c =>
                c.TableName.StartsWith("sql:", StringComparison.Ordinal)
                && c.Html.TrimStart().StartsWith('{')
            )
            .ToList();
}
