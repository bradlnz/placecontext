using PlaceContext.Domain.Common;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a conversation session between a user and the project's chat agent.
/// Project-scoped, per-user (via ICurrentUser). Messages are stored as a flat JSONB list
/// for simplicity in Phase 1; later phases may normalize them.
/// </summary>
public sealed class AgentChatSession : AggregateRoot
{
    private readonly List<AgentMessage> _messages = new();

    private AgentChatSession(
        Guid id,
        Guid projectId,
        Guid? userId,
        string? title,
        IReadOnlyList<AgentMessage> messages,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        UserId = userId;
        Title = title;
        _messages.AddRange(messages);
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid? UserId { get; }
    public string? Title { get; private set; }
    public IReadOnlyList<AgentMessage> Messages => _messages;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory: creates a new empty session.</summary>
    public static AgentChatSession Create(Guid projectId, Guid? userId, string? title, DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));

        return new AgentChatSession(Guid.NewGuid(), projectId, userId, title, Array.Empty<AgentMessage>(), now, now);
    }

    /// <summary>Rehydrates from persistence. Infrastructure only.</summary>
    public static AgentChatSession Rehydrate(
        Guid id, Guid projectId, Guid? userId, string? title,
        IReadOnlyList<AgentMessage> messages,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, userId, title, messages, createdAt, updatedAt);

    /// <summary>Appends a user message and an assistant reply to the session.</summary>
    public void AppendMessages(string userContent, string assistantContent, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userContent))
            throw new ArgumentException("User message must not be empty.", nameof(userContent));
        if (string.IsNullOrWhiteSpace(assistantContent))
            throw new ArgumentException("Assistant message must not be empty.", nameof(assistantContent));

        _messages.Add(new AgentMessage("user", userContent.Trim(), now));
        _messages.Add(new AgentMessage("assistant", assistantContent.Trim(), now));

        // Auto-title from the first user message if no title is set.
        Title ??= userContent.Length > 80 ? userContent[..80] + "…" : userContent;
        UpdatedAt = now;
    }
}
