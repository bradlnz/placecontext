using System.Text.Json.Serialization;

namespace PlaceContext.Application.Cluster;

public sealed class ClusterMessageDto
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}
