namespace PlaceContext.Application.Auth;

public sealed record SsoEndpoints(string AuthorizationEndpoint, string TokenEndpoint, string UserInfoEndpoint, string Scope);
