using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>A fake chat gateway that echoes messages back for deterministic tests.</summary>
public sealed class FakeChatGateway : IChatGateway
{
    public bool IsEnabled { get; set; } = true;
    public List<ChatMessage> LastMessages { get; } = new();
    public ChatSettings? LastSettings { get; private set; }
    public string ReplyToReturn { get; set; } = "I am a helpful assistant.";

    public Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatSettings? settings = null, CancellationToken ct = default)
    {
        LastMessages.AddRange(messages);
        LastSettings = settings;
        return Task.FromResult(ReplyToReturn);
    }
}
