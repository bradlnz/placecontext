using System.Text.Json.Serialization;

namespace PlaceContext.Application.Mcp;

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken
    {
        get; set;
    }
    [JsonPropertyName("expires_in")]
    public long ExpiresIn
    {
        get; set;
    }
    [JsonPropertyName("token_type")]
    public string? TokenType
    {
        get; set;
    }
}
