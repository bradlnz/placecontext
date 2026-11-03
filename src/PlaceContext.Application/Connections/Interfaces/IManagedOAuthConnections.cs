namespace PlaceContext.Application.Connections;

/// <summary>Server-owned delegated OAuth connections. OAuth access tokens are never included
/// in the connection view or returned to browsers; token resolution is for the job runner only.</summary>
public interface IManagedOAuthConnections
{
    Task<Uri> BeginAsync(Guid projectId, CancellationToken ct = default);
    Task<Guid> CompleteAsync(string state, string authorizationCode, CancellationToken ct = default);
    Task<IReadOnlyList<ManagedConnectionView>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task DisconnectAsync(Guid projectId, CancellationToken ct = default);
    Task<string> ResolveForJobAsync(Guid projectId, CancellationToken ct = default);
}
