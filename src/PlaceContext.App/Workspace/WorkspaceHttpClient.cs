using System.Text.Json;
using PlaceContext.App.Proxy;

namespace PlaceContext.App.Workspace;

public sealed class WorkspaceHttpClient(EdgeHttpClient http) : IWorkspaceHttpClient
{
    public Task<JsonElement> GetProjectsAsync(string callerToken, CancellationToken cancellationToken)
        => http.GetAsync("Projects", "api/projects/internal", callerToken, cancellationToken, useApiKey: true);

    public Task<JsonElement> GetFocusAsync(string callerToken, CancellationToken cancellationToken)
        => http.GetAsync("Projects", "api/projects/internal/focus", callerToken, cancellationToken, useApiKey: true);

    public Task<JsonElement> GetStatsAsync(string callerToken, CancellationToken cancellationToken)
        => http.GetAsync("Projects", "api/projects/internal/stats", callerToken, cancellationToken, useApiKey: true);
}
