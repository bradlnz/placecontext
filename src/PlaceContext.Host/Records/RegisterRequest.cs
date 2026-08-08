using System.Text.Json.Serialization;

namespace PlaceContext.Host.Auth;

internal sealed record RegisterRequest(
    [property: JsonPropertyName("redirect_uris")] string[]? RedirectUris,
    [property: JsonPropertyName("client_name")] string? ClientName);
