using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Features;

namespace PlaceContext.Application.Build;

public static class DashboardChartData
{
    public static async Task<ChartSpec?> ReadAsync(PlaceContextService service, IPermissionService permissions,
        Guid projectId, string title, string type, string? sql, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || type is not ("bar" or "line" or "pie")
            || sql is not null && (string.IsNullOrWhiteSpace(sql) || sql.Length > 32768))
            throw new ArgumentException("Provide a chart title, a bar, line or pie type, and a read-only query of up to 32,768 characters.");
        var permission = sql is null ? Permission.JobsView : Permission.DataRead;
        if (!await permissions.HasAsync(Permission.ProjectsView, ct) || !await permissions.HasAsync(permission, ct))
            throw new UnauthorizedAccessException("You no longer have access to this chart's data.");
        if (await service.GetProjectByIdAsync(projectId, ct) is null)
            throw new InvalidOperationException("Project not found.");
        if (sql is null)
        {
            var counts = await service.GetProjectRunCountsAsync(projectId, ct);
            return new(type, title, ["Running", "Queued", "Failed (24h)", "Succeeded (24h)"],
                [new("Runs", [counts.GetValueOrDefault("Running"), counts.GetValueOrDefault("Queued"),
                    counts.GetValueOrDefault("Failed"), counts.GetValueOrDefault("Succeeded")])]);
        }
        var result = await service.QueryProjectDataAsync(projectId, sql, ct);
        if (result.Rows.Count == 0) return null;
        return ChartSpec.TryParse(SqlChartDefinition.Build(title, sql, type, result))
            ?? throw new InvalidOperationException("Return a label column and finite numeric values to chart.");
    }
}
