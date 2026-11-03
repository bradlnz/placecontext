using System.Text.Json.Serialization;

namespace PlaceContext.Application.Mcp;

public sealed class OAuthMetadata
{
    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint
    {
        get; set;
    }
    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint
    {
        get; set;
    }
    [JsonPropertyName("registration_endpoint")]
    public string? RegistrationEndpoint
    {
        get; set;
    }
}
