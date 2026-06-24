using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// An append-only record of LLM token usage attributed to a project — the raw material for the cost
/// dashboards. Immutable; created through <see cref="Record"/> (new) or <see cref="Rehydrate"/> (load).
/// PlaceContext stores only this metadata (model + token counts), never any code or prompt content.
/// </summary>
public sealed class UsageRecord
{
    private UsageRecord(Guid id, ProjectId projectId, TokenUsage usage, string? description, DateTimeOffset recordedAt)
    {
        Id = id;
        ProjectId = projectId;
        Usage = usage;
        Description = description;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; }
    public ProjectId ProjectId { get; }
    public TokenUsage Usage { get; }
    /// <summary>Optional free-text label (e.g. "review pass", "feature X"). Never code or prompt content.</summary>
    public string? Description { get; }
    public DateTimeOffset RecordedAt { get; }

    public static UsageRecord Record(ProjectId projectId, TokenUsage usage, string? description, DateTimeOffset recordedAt)
        => new(Guid.NewGuid(), projectId, usage, string.IsNullOrWhiteSpace(description) ? null : description.Trim(), recordedAt);

    public static UsageRecord Rehydrate(Guid id, ProjectId projectId, TokenUsage usage, string? description, DateTimeOffset recordedAt)
        => new(id, projectId, usage, description, recordedAt);
}
