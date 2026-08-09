namespace PlaceContext.Application.Ports;

/// <summary>
/// Turns a list of chat messages into an LLM completion. Implemented in Infrastructure (Ollama)
/// with a Null fallback when no local model is configured. The chat agent handler depends on this
/// port to talk to whatever SLM/LLM is available.
/// </summary>
public interface IChatGateway
{
    /// <summary>Whether a real chat backend is configured. When false, callers return a friendly refusal.</summary>
    bool IsEnabled { get; }

    /// <summary>Sends the conversation to the model and returns the assistant's reply text.</summary>
    Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default);

    /// <summary>Streams completion chunks when the backend supports streaming.</summary>
    async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatSettings? settings = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await ChatAsync(messages, settings, ct);
        if (!string.IsNullOrEmpty(response))
            yield return response;
    }
}
