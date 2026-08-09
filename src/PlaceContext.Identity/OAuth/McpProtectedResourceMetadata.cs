using System.Text.Json.Serialization;

namespace PlaceContext.Identity.OAuth;

internal sealed class McpProtectedResourceMetadata
{
    [JsonPropertyName("authorization_servers")]
    public string[]? AuthorizationServers { get; set; }
}
