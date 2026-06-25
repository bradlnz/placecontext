using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Embeddings;

/// <summary>The default <see cref="IEmbeddingGateway"/> when no Voyage API key is configured. Disabled,
/// so run output is never embedded and semantic search returns nothing.</summary>
public sealed class NullEmbeddingGateway : IEmbeddingGateway
{
    public bool IsEnabled => false;
    public int Dimensions => 0;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float[]>>(Array.Empty<float[]>());
}
