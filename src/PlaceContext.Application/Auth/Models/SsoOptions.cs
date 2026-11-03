namespace PlaceContext.Application.Auth;

public sealed record SsoOptions(Uri BaseUri, string ClientId, string CallbackUrl, bool OpenIdConnect);
