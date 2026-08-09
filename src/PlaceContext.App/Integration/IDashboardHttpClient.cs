namespace PlaceContext.App.Dashboard;

public interface IDashboardHttpClient
{
    Task<DashboardResponse> GetAsync(Guid? projectId, string callerToken, CancellationToken cancellationToken);
    Task<RunChainResponse> RunChainAsync(
        Guid projectId,
        Guid chainId,
        RunChainRequest? request,
        string callerToken,
        CancellationToken cancellationToken);
}
