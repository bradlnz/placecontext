namespace PlaceContext.Infrastructure.Caching;

/// <summary>In-memory fallback when Redis is not configured. Sessions are lost on restart.</summary>
public sealed class NullChatMemoryStore : IChatMemoryStore
{
    private readonly Dictionary<Guid, ChatSessionMemory> _sessions = new();
    private readonly Dictionary<Guid, List<ChatSessionSummary>> _projectSessions = new();

    public Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(Guid projectId, CancellationToken ct = default)
    {
        _projectSessions.TryGetValue(projectId, out var summaries);
        return Task.FromResult<IReadOnlyList<ChatSessionSummary>>(summaries?.OrderByDescending(s => s.LastMessageAt).ToList() ?? new List<ChatSessionSummary>());
    }

    public Task<ChatSessionMemory?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        _sessions.TryGetValue(sessionId, out var memory);
        return Task.FromResult(memory);
    }

    public Task SaveSessionAsync(Guid sessionId, ChatSessionMemory memory, CancellationToken ct = default)
    {
        _sessions[sessionId] = memory;
        if (!_projectSessions.TryGetValue(memory.ProjectId, out var summaries))
        {
            summaries = new();
            _projectSessions[memory.ProjectId] = summaries;
        }
        var existing = summaries.FindIndex(s => s.Id == sessionId);
        var summary = new ChatSessionSummary(sessionId, memory.ProjectId, memory.Title, memory.Messages.Count, memory.CreatedAt, memory.LastMessageAt);
        if (existing >= 0) summaries[existing] = summary;
        else summaries.Add(summary);
        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (_sessions.Remove(sessionId, out var session))
        {
            if (_projectSessions.TryGetValue(session.ProjectId, out var summaries))
                summaries.RemoveAll(s => s.Id == sessionId);
        }
        return Task.CompletedTask;
    }

    public Task ClearSessionMemoryAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            _sessions[sessionId] = session with { Messages = new() };
        return Task.CompletedTask;
    }
}
