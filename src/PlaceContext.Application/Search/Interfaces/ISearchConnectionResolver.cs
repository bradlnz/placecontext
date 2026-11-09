namespace PlaceContext.Application.Search;

/// <summary>Resolves the OpenSearch connection without exposing credentials to UI read models.</summary>
public interface ISearchConnectionResolver
{
    Task<SearchConnection?> ResolveAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetJobEnvironmentAsync(
        Guid projectId, CancellationToken ct = default);
}
