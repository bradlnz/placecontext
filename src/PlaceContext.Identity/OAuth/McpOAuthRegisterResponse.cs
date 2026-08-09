using System.Text.Json.Serialization;

namespace PlaceContext.Identity.OAuth;

internal sealed class McpOAuthRegisterResponse
{
    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }
}
