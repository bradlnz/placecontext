namespace PlaceContext.Identity.Contracts.Api;

public sealed record ServiceTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
