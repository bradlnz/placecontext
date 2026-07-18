namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one expandable entry on the cross-project Change Activity.</summary>
public sealed record ActivityEntryView(
    Guid Id,
    int Sequence,
    string Project,
    string Author,
    string Kind,
    string Title,
    string Why,
    bool HasRationale,
    string TestDelta,
    string? Commit,
    int FileCount,
    IReadOnlyList<string> Files,
    DateTimeOffset RecordedAt);
