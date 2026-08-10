using PlaceContext.Application.Ports;

namespace PlaceContext.TestSupport;

/// <summary>A fake chat gateway that echoes messages back for deterministic tests.</summary>
public sealed class FakeChatGateway : IChatGateway, IProjectChatGateway
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

    public Task<ProjectChatStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(new ProjectChatStatus(
            IsEnabled ? ProjectChatBackend.LocalCluster : ProjectChatBackend.None,
            IsEnabled,
            IsEnabled ? "Local agent cluster" : "No model configured"));

    public Task<string> ChatAsync(Guid projectId, IReadOnlyList<ChatMessage> messages,
        ChatSettings? settings = null, CancellationToken ct = default)
        => ChatAsync(messages, settings, ct);
}
