using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a Markdown document of code requirements — the coding standards an agent must
/// follow and that the review/skill prompts check against. There is one <b>global</b> document
/// (applies to every project) and optionally one <b>per project</b>. A project's <i>effective</i>
/// requirements are the global document followed by the project's own (assembled in the Application
/// layer). Mirrors <see cref="ProjectContext"/>: appendless, replaced wholesale from the portal.
/// </summary>
public sealed class CodeRequirements : AggregateRoot
{
    private CodeRequirements(ProjectId? projectId, string markdown, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        ProjectId = projectId;
        Markdown = markdown;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>The project these requirements belong to, or <c>null</c> for the global document.</summary>
    public ProjectId? ProjectId { get; }
    public string Markdown { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsGlobal => ProjectId is null;
    public bool IsEmpty => string.IsNullOrWhiteSpace(Markdown);

    /// <summary>An empty global requirements document.</summary>
    public static CodeRequirements StartGlobal(DateTimeOffset now) => new(null, string.Empty, now, now);

    /// <summary>An empty per-project requirements document.</summary>
    public static CodeRequirements StartForProject(ProjectId projectId, DateTimeOffset now)
        => new(projectId, string.Empty, now, now);

    /// <summary>Rebuilds the document from persisted state. Used only by the Infrastructure repository.</summary>
    public static CodeRequirements Rehydrate(
        ProjectId? projectId, string markdown, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(projectId, markdown ?? string.Empty, createdAt, updatedAt);

    /// <summary>Replaces the whole document (the portal saves the edited requirements).</summary>
    public void Replace(string markdown, DateTimeOffset now)
    {
        Markdown = markdown ?? string.Empty;
        UpdatedAt = now;
    }
}
