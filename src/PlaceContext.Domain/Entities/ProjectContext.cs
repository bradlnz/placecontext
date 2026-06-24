using PlaceContext.Domain.Common;
using PlaceContext.Domain.Events;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: the single, appendable Markdown context document for a project — the durable
/// knowledge a coding agent fetches before working and adds to as it learns. One per project,
/// identified by its <see cref="ProjectId"/>. The body is plain Markdown; appends are separated by a
/// blank line so the document reads as a running set of sections.
/// </summary>
public sealed class ProjectContext : AggregateRoot
{
    private ProjectContext(ProjectId projectId, string markdown, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        ProjectId = projectId;
        Markdown = markdown;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public ProjectId ProjectId { get; }
    public string Markdown { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Markdown);

    /// <summary>An empty context document for a project.</summary>
    public static ProjectContext Start(ProjectId projectId, DateTimeOffset now)
        => new(projectId, string.Empty, now, now);

    /// <summary>Rebuilds the document from persisted state. Used only by the Infrastructure repository.</summary>
    public static ProjectContext Rehydrate(
        ProjectId projectId, string markdown, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new(projectId, markdown ?? string.Empty, createdAt, updatedAt);

    /// <summary>Appends a Markdown section, separated from existing content by a blank line.</summary>
    public void Append(string section, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(section))
            throw new ArgumentException("Context section must not be empty.", nameof(section));

        var trimmed = section.Trim();
        Markdown = IsEmpty ? trimmed : Markdown + "\n\n" + trimmed;
        UpdatedAt = now;
        Raise(new ProjectContextUpdated(ProjectId, now));
    }

    /// <summary>Replaces the whole document (e.g. the agent rewrites the context).</summary>
    public void Replace(string markdown, DateTimeOffset now)
    {
        Markdown = markdown ?? string.Empty;
        UpdatedAt = now;
        Raise(new ProjectContextUpdated(ProjectId, now));
    }
}
