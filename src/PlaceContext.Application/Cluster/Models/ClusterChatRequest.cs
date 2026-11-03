using System.Text.Json.Serialization;

namespace PlaceContext.Application.Cluster;

public sealed class ClusterChatRequest
{
    [JsonPropertyName("messages")] public List<ClusterMessageDto> Messages { get; set; } = new();
    [JsonPropertyName("temperature")]
    public float? Temperature
    {
        get; set;
    }
    [JsonPropertyName("top_p")]
    public float? TopP
    {
        get; set;
    }
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens
    {
        get; set;
    }
}
