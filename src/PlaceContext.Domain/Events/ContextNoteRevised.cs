using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Events;

/// <summary>A context note was created or revised.</summary>
public sealed record ContextNoteRevised(ProjectId ProjectId, ContextNoteId NoteId, DateTimeOffset OccurredAt)
    : IDomainEvent;
