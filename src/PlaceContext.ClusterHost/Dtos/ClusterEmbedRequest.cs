using System.Text.Json.Serialization;

namespace PlaceContext.ClusterHost;

public sealed class ClusterEmbedRequest
{
    [JsonPropertyName("input")] public List<string> Input { get; set; } = new();
}
