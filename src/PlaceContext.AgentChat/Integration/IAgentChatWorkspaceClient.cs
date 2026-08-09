namespace PlaceContext.AgentChat.Integration;

public interface IAgentChatWorkspaceClient
{
    Task<string> BuildContextAsync(
        Guid projectId,
        string userMessage,
        int maxChunks,
        CancellationToken ct = default);

    Task<string> ExecuteToolAsync(
        Guid projectId,
        string toolName,
        string args,
        CancellationToken ct = default);

    Task<AgentChatTablePage> QueryTablePageAsync(
        Guid projectId,
        string tableName,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
