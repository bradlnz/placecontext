using System.Text.Json.Serialization;

namespace PlaceContext.Host.Controllers;

internal sealed class McpOAuthRegisterResponse
{
    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }
}
