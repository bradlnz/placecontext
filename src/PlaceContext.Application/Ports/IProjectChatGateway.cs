namespace PlaceContext.Application.Ports;

/// <summary>Resolves a project's preferred model without exposing Vault secrets to callers.</summary>
public interface IProjectChatGateway
{
    Task<ProjectChatStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default);
    Task<string> ChatAsync(
        Guid projectId,
        IReadOnlyList<ChatMessage> messages,
        ChatSettings? settings = null,
        CancellationToken ct = default);
}
