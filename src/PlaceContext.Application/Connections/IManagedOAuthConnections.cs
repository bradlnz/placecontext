namespace PlaceContext.Application.Connections;

public sealed record ManagedConnectionView(
    Guid ProjectId, string Provider, string Name, string Scopes, DateTimeOffset ExpiresAt);

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

public sealed class ManagedMicrosoftOAuthOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Exact callback registered as a Web redirect URI in Microsoft Entra ID.</summary>
    public string RedirectUri { get; set; } = "";
    /// <summary>Use a tenant-specific Entra authority if a single-tenant app is desired.</summary>
    public string Authority { get; set; } = "organizations";
}
