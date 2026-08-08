namespace PlaceContext.Application.Ports;

/// <summary>
/// Resolves the workspace represented by an original edge request host. This is used only for
/// legacy API-key requests, which do not carry the signed tenant claims used by service JWTs.
/// </summary>
public interface IRequestTenantResolver
{
    Task<TenantContext?> ResolveAsync(string host, CancellationToken ct = default);
}
