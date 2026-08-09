namespace PlaceContext.Identity.OAuth;

public interface IMcpOAuthConnectionClient
{
    Task<McpOAuthConnectionContext?> GetAsync(
        Guid connectionId,
        IdentityTenantContext tenant,
        CancellationToken ct = default);

    Task StoreTokensAsync(
        Guid connectionId,
        StoreMcpOAuthTokensRequest request,
        IdentityTenantContext tenant,
        CancellationToken ct = default);

    Task UpdateStatusAsync(
        Guid connectionId,
        UpdateMcpOAuthStatusRequest request,
        IdentityTenantContext tenant,
        CancellationToken ct = default);
}
