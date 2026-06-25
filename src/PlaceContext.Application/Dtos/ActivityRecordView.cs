using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one row on the change timeline.</summary>
public sealed record ActivityRecordView(
    Guid Id,
    int Sequence,
    string Title,
    string Author,
    string Kind,
    string Rationale,
    string? Commit,
    bool HasTests,
    bool ArchReviewed,
    bool LiveVerified,
    IReadOnlyList<string> TouchedFiles,
    DateTimeOffset RecordedAt);
