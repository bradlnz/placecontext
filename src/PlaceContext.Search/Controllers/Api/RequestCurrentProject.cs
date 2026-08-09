using Microsoft.AspNetCore.Http;
using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Controllers.Api;

/// <summary>
/// Resolves GUID project hints at the independently hosted Search boundary. The legacy Host's
/// name-aware project resolver is registered later and takes precedence while that host remains.
/// </summary>
public sealed class RequestCurrentProject(IHttpContextAccessor httpContextAccessor) : ICurrentProject
{
    public Guid ProjectId => ResolveProjectId();

    public string ProjectName
    {
        get
        {
            var request = httpContextAccessor.HttpContext?.Request;
            if (request is null) return string.Empty;

            var name = request.Headers["X-Project"].ToString();
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();

            name = request.Query["project"].ToString();
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        }
    }

    public bool IsResolved => ProjectId != Guid.Empty;

    private Guid ResolveProjectId()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null) return Guid.Empty;

        var candidates = new[]
        {
            request.Headers["X-Project-Id"].ToString(),
            request.Headers["X-Project"].ToString(),
            request.Query["projectId"].ToString(),
            request.Query["project"].ToString(),
        };
        foreach (var candidate in candidates)
        {
            if (Guid.TryParse(candidate?.Trim(), out var projectId))
                return projectId;
        }

        return Guid.Empty;
    }
}
