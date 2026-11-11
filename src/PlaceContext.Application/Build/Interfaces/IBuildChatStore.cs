using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public interface IBuildChatStore
{
    Task<IReadOnlyList<BuildConversationSummary>> ListAsync(Guid projectId, CancellationToken ct = default);
    Task<BuildConversation> LoadAsync(Guid projectId, Guid id, CancellationToken ct = default);
    Task QueueAsync(BuildConversation conversation, JsonObject message, CancellationToken ct = default);
    Task RequestStopAsync(Guid projectId, Guid id, CancellationToken ct = default);
    Task<Guid> AcquireAsync(BuildConversation conversation, CancellationToken ct = default);
    Task<bool> RenewAsync(Guid projectId, Guid id, Guid lease, CancellationToken ct = default);
    Task SaveAsync(BuildConversation conversation, Guid lease, bool release, CancellationToken ct = default);
    Task DeleteAsync(Guid projectId, Guid id, CancellationToken ct = default);
    Task<string> GetMemoryAsync(Guid projectId, CancellationToken ct = default);
    Task SaveMemoryAsync(Guid projectId, string memory, CancellationToken ct = default);
    Task<DashboardSettings> GetSettingsAsync(Guid projectId, CancellationToken ct = default);
    Task SaveSettingsAsync(Guid projectId, DashboardSettings settings, CancellationToken ct = default);
}
