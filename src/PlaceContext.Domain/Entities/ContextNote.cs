using PlaceContext.Domain.Common;
using PlaceContext.Domain.Events;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a piece of human/agent context describing part of a project's code. Carries the
/// graph nodes it is about and a digest of the code it described when written, so the staleness
/// policy can tell when the code has drifted underneath it.
/// </summary>
public sealed class ContextNote : AggregateRoot
{
    private readonly List<GraphNodeId> _subjects;

    private ContextNote(
        ContextNoteId id,
        ProjectId projectId,
        string title,
        string body,
        IEnumerable<GraphNodeId> subjects,
        ContentDigest describedCodeDigest,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Title = title;
        Body = body;
        _subjects = subjects.ToList();
        DescribedCodeDigest = describedCodeDigest;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public ContextNoteId Id { get; }
    public ProjectId ProjectId { get; }
    public string Title { get; private set; }
    public string Body { get; private set; }
    public IReadOnlyList<GraphNodeId> Subjects => _subjects;
    public ContentDigest DescribedCodeDigest { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ContextNote Write(
        ProjectId projectId,
        string title,
        string body,
        IEnumerable<GraphNodeId> subjects,
        ContentDigest describedCodeDigest,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("ContextNote title must not be empty.", nameof(title));

        var note = new ContextNote(
            ContextNoteId.New(), projectId, title.Trim(), body ?? string.Empty,
            subjects, describedCodeDigest, now, now);
        note.Raise(new ContextNoteRevised(projectId, note.Id, now));
        return note;
    }

    public static ContextNote Rehydrate(
        ContextNoteId id,
        ProjectId projectId,
        string title,
        string body,
        IEnumerable<GraphNodeId> subjects,
        ContentDigest describedCodeDigest,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        => new(id, projectId, title, body, subjects, describedCodeDigest, createdAt, updatedAt);

    /// <summary>Revises the note body and re-anchors it to the current code digest.</summary>
    public void Revise(string body, ContentDigest currentDigest, DateTimeOffset now)
    {
        Body = body ?? string.Empty;
        DescribedCodeDigest = currentDigest;
        UpdatedAt = now;
        Raise(new ContextNoteRevised(ProjectId, Id, now));
    }
}
