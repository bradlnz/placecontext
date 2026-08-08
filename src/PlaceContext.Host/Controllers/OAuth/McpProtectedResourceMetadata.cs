using System.Text.Json.Serialization;

namespace PlaceContext.Host.Controllers;

internal sealed class McpProtectedResourceMetadata
{
    [JsonPropertyName("authorization_servers")]
    public string[]? AuthorizationServers { get; set; }
}
