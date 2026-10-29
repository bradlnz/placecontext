using System.Text.Json.Nodes;

namespace PlaceContext.Application.Build;

public interface IModelConnections
{
    Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default);
}

public interface IBuildModelGateway
{
    Task<JsonObject> CompleteAsync(Guid connectionId, string model, JsonArray messages, JsonArray tools, CancellationToken ct);
}

public enum BuildRunStatus { Idle, Queued, Running, Completed, Stopped, Failed, Interrupted }

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
}
