using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Ports;

/// <summary>Resolves the OpenSearch connection without exposing credentials to UI read models.</summary>
public interface IOpenSearchConnectionResolver
{
    Task<OpenSearchConnection?> ResolveAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetJobEnvironmentAsync(
        Guid projectId, CancellationToken ct = default);
}
