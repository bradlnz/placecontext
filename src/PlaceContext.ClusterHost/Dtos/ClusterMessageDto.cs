using System.Text.Json.Serialization;

namespace PlaceContext.ClusterHost;

public sealed class ClusterMessageDto
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}
