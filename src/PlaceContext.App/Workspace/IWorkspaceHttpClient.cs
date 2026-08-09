using System.Text.Json;

namespace PlaceContext.App.Workspace;

public interface IWorkspaceHttpClient
{
    Task<JsonElement> GetProjectsAsync(string callerToken, CancellationToken cancellationToken);
    Task<JsonElement> GetFocusAsync(string callerToken, CancellationToken cancellationToken);
    Task<JsonElement> GetStatsAsync(string callerToken, CancellationToken cancellationToken);
}
