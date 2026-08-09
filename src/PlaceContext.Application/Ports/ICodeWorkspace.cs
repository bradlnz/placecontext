namespace PlaceContext.Application.Ports;

/// <summary>Clones repositories into a per-tenant local workspace for project creation.</summary>
public interface ICodeWorkspace
{
    /// <summary>Clones (or updates) a repo into the tenant's workspace and returns the local path.</summary>
    Task<string> CloneAsync(string tenantSlug, string cloneUrl, string repoName, string? accessToken, CancellationToken ct = default);
}
