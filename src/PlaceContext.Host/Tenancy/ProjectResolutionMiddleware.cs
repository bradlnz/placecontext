using PlaceContext.Application;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Tenancy;

/// <summary>
/// Resolves the ambient project for machine callers of the entity data API. Accepts, in order:
/// <list type="number">
///   <item><c>X-Project-Id</c> header (GUID)</item>
///   <item><c>X-Project</c> header (project name, case-insensitive)</item>
///   <item><c>projectId</c> / <c>project</c> query string (same shapes)</item>
/// </list>
/// Missing headers are fine — portal/MCP paths don't need a project. Entity and search endpoints
/// require it and return 400 when unresolved. Runs after tenant resolution so the project lookup is
/// tenant-scoped by EF's global filter.
/// </summary>
public sealed class ProjectResolutionMiddleware
{
    public const string ProjectIdHeader = "X-Project-Id";
    public const string ProjectHeader = "X-Project";

    private readonly RequestDelegate _next;
    public ProjectResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, IPlaceContextService svc)
    {
        try
        {
            var (idRaw, nameRaw) = ReadHints(context.Request);
            if (idRaw is not null || nameRaw is not null)
            {
                var project = await ResolveAsync(svc, idRaw, nameRaw, context.RequestAborted);
                if (project is not null)
                    CurrentProject.Set(project.Id, project.Name);
            }

            await _next(context);
        }
        finally
        {
            CurrentProject.Clear();
        }
    }

    private static (string? Id, string? Name) ReadHints(HttpRequest request)
    {
        var id = HeaderOrQuery(request, ProjectIdHeader, "projectId");
        var name = HeaderOrQuery(request, ProjectHeader, "project");
        return (EmptyToNull(id), EmptyToNull(name));
    }

    private static string? HeaderOrQuery(HttpRequest request, string header, string query)
    {
        var h = request.Headers[header].ToString();
        if (!string.IsNullOrWhiteSpace(h)) return h.Trim();
        var q = request.Query[query].ToString();
        return string.IsNullOrWhiteSpace(q) ? null : q.Trim();
    }

    private static string? EmptyToNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;

    private static async Task<Application.Dtos.ProjectSummaryView?> ResolveAsync(
        IPlaceContextService svc, string? idRaw, string? nameRaw, CancellationToken ct)
    {
        if (idRaw is not null && Guid.TryParse(idRaw, out var id))
            return await svc.GetProjectByIdAsync(id, ct);

        if (nameRaw is null) return null;

        // Name may also be a GUID string when clients only set X-Project.
        if (Guid.TryParse(nameRaw, out var asId))
            return await svc.GetProjectByIdAsync(asId, ct);

        var all = await svc.GetProjectsAsync(ct);
        return all.FirstOrDefault(p =>
            string.Equals(p.Name, nameRaw, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Path, nameRaw, StringComparison.OrdinalIgnoreCase)
            || p.Path.EndsWith("/" + nameRaw, StringComparison.OrdinalIgnoreCase)
            || p.Path.EndsWith("\\" + nameRaw, StringComparison.OrdinalIgnoreCase));
    }
}
