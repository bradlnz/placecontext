using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Infrastructure.Chat;

/// <summary>
/// The default <see cref="IChatGateway"/> when no Ollama endpoint is configured. Disabled,
/// so chat attempts return a clear "no model available" message.
/// </summary>
public sealed class NullChatGateway : IChatGateway
{
    public bool IsEnabled => false;

    public Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default)
        => Task.FromResult("No local language model is configured. Set PlaceContext:Chat:Endpoint to enable the chat agent.");
}
