using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>
/// Deterministic <see cref="IEmbeddingGateway"/> for tests: maps known texts to fixed vectors (unknown
/// texts embed to a zero vector). Enabled by default.
/// </summary>
public sealed class FakeEmbeddingGateway : IEmbeddingGateway
{
    private readonly Dictionary<string, float[]> _map;

    public FakeEmbeddingGateway(bool enabled = true, int dimensions = 3)
    {
        IsEnabled = enabled;
        Dimensions = dimensions;
        _map = new Dictionary<string, float[]>();
    }

    public bool IsEnabled { get; }
    public int Dimensions { get; }

    public void Set(string text, float[] vector) => _map[text] = vector;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float[]>>(
            texts.Select(t => _map.TryGetValue(t, out var v) ? v : new float[Dimensions]).ToList());
}
