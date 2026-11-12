namespace PlaceContext.Application.Build;

public interface IConversationShares
{
    Task<WorkflowDemoLink> CreateAsync(Guid projectId, Guid conversationId, Guid? runId, IReadOnlyList<Guid> artifactIds, int lifetimeDays, CancellationToken ct = default);
    Task<DateTimeOffset?> GetExpiryAsync(Guid projectId, Guid conversationId, CancellationToken ct = default);
    Task RevokeAsync(Guid projectId, Guid conversationId, CancellationToken ct = default);
    Task<SharedConversation?> ResolveAsync(string token, CancellationToken ct = default);
}
