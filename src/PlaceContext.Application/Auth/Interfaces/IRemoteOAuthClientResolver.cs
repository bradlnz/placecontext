namespace PlaceContext.Application.Auth;

public interface IRemoteOAuthClientResolver
{
    Task<OAuthClient?> ResolveAsync(string id, string redirect, CancellationToken ct);
}
