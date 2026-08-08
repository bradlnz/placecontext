using System.Text.Json.Serialization;

namespace PlaceContext.AgentChat.Infrastructure.Chat;

internal sealed class ClusterChatRequestDto
{
    [JsonPropertyName("messages")]
    public List<ClusterChatMessageDto> Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }
}
