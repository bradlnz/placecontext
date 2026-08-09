namespace PlaceContext.App.Proxy;

public sealed record EdgeServiceTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
