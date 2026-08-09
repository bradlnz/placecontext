using PlaceContext.AgentChat.Integration;

namespace PlaceContext.Application.Tests.Agents;

internal sealed class FakeAgentChatWorkspaceClient : IAgentChatWorkspaceClient
{
    public string ContextToReturn { get; set; } = string.Empty;
    public AgentChatTablePage TablePageToReturn { get; set; } = new(
        Array.Empty<string>(), Array.Empty<IReadOnlyList<string?>>(), 0);

    public (Guid ProjectId, string UserMessage, int MaxChunks, CancellationToken CancellationToken)?
        LastBuildContextCall { get; private set; }

    public Task<string> BuildContextAsync(
        Guid projectId,
        string userMessage,
        int maxChunks,
        CancellationToken ct = default)
    {
        LastBuildContextCall = (projectId, userMessage, maxChunks, ct);
        return Task.FromResult(ContextToReturn);
    }

    public Task<string> ExecuteToolAsync(
        Guid projectId,
        string toolName,
        string args,
        CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<AgentChatTablePage> QueryTablePageAsync(
        Guid projectId,
        string tableName,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => Task.FromResult(TablePageToReturn);
}
