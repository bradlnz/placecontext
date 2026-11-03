namespace PlaceContext.Application.Cluster;

public interface IClusterInference
{
    bool IsEnabled
    {
        get;
    }
    Task<string> GenerateAsync(ClusterChatRequest request, CancellationToken ct);
    IAsyncEnumerable<string> GenerateStreamAsync(ClusterChatRequest request, CancellationToken ct);
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> input, CancellationToken ct);
}
