namespace PlaceContext.Identity.Contracts.Api;

public sealed record IdentityServiceStatus(string Service, string Status, bool OAuthEnabled);
