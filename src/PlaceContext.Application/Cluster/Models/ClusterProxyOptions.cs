namespace PlaceContext.Application.Cluster;

public sealed class ClusterProxyOptions
{
    public List<string> ShardEndpoints { get; set; } = new();
    public string Model { get; set; } = "qwen3.5-4b";
    public string ApiToken { get; set; } = "";
}
