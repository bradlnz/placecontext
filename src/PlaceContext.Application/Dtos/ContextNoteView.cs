using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record ContextNoteView(Guid Id, string Title, string Body, bool IsStale, DateTimeOffset UpdatedAt);
