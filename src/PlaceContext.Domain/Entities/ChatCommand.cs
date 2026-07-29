using PlaceContext.Domain.Common;

namespace PlaceContext.Domain.Entities;

public sealed class ChatCommand : AggregateRoot
{
    private ChatCommand(
        Guid id, Guid projectId, string name, string? description,
        string toolName, string? args,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Description = description;
        ToolName = toolName;
        Args = args;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string ToolName { get; private set; }
    public string? Args { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChatCommand Create(
        Guid projectId, string name, string? description,
        string toolName, string? args, DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name must not be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name must not be empty.", nameof(toolName));

        return new ChatCommand(
            Guid.NewGuid(), projectId, name.Trim(), string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            toolName.Trim(), args?.Trim(),
            now, now);
    }

    public void Update(string name, string? description, string toolName, string? args, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name must not be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name must not be empty.", nameof(toolName));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ToolName = toolName.Trim();
        Args = args?.Trim();
        UpdatedAt = now;
    }

    public static ChatCommand Rehydrate(
        Guid id, Guid projectId, string name, string? description,
        string toolName, string? args,
        DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(id, projectId, name, description, toolName, args, createdAt, updatedAt);
}
