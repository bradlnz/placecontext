using PlaceContext.App.Proxy;

namespace PlaceContext.App.Authentication;

/// <summary>
/// Caller-local identity projected from the short-lived token returned by Identity's existing
/// cookie-to-service-token adapter. The token is never accepted from caller input.
/// </summary>
public sealed class EdgeCallerContext(EdgeServiceTokenClient serviceTokens)
{
    private Task<string?>? _exchange;

    public async Task<EdgeCallerIdentity?> AuthenticateAsync(HttpContext context)
    {
        var token = await (_exchange ??= serviceTokens.ExchangeAsync(context));
        return token is null ? null : EdgeCallerIdentity.FromServiceToken(token);
    }
}
