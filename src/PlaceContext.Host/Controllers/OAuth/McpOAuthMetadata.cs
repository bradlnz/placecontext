using System.Text.Json.Serialization;

namespace PlaceContext.Host.Controllers;

internal sealed class McpOAuthMetadata
{
    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("registration_endpoint")]
    public string? RegistrationEndpoint { get; set; }
}
