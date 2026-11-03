namespace PlaceContext.Application.Auth;

public interface IExternalIdentityProvider
{
    Task<SsoEndpoints> ResolveEndpointsAsync(SsoOptions options, CancellationToken ct);
    Task<ExternalIdentity> ExchangeAsync(SsoOptions options, string code, string verifier, CancellationToken ct);
}
