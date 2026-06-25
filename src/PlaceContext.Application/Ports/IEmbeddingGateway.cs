namespace PlaceContext.Application.Ports;

/// <summary>
/// Turns text into embedding vectors. Implemented in Infrastructure (Voyage AI) with a Null fallback
/// when no API key is configured, mirroring <see cref="ILlmGateway"/>. Used to vectorize organized job
/// output so runs become semantically searchable and linkable in the dependency graph.
/// </summary>
public interface IEmbeddingGateway
{
    /// <summary>Whether a real embedding backend is configured. When false, callers skip embedding.</summary>
    bool IsEnabled { get; }

    /// <summary>The dimensionality of the produced vectors (0 when disabled).</summary>
    int Dimensions { get; }

    /// <summary>Embeds each input text, returning one vector per input (in order). Only call when enabled.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
