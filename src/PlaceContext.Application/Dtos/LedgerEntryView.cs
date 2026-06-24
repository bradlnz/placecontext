namespace PlaceContext.Application.Dtos;

/// <summary>Read model: one expandable entry on the cross-project Change Ledger.</summary>
public sealed record LedgerEntryView(
    Guid Id,
    int Sequence,
    string Project,
    string Author,
    string Kind,
    string Title,
    string Why,
    bool HasRationale,
    string TestDelta,
    int DebtNet,
    string? Commit,
    int FileCount,
    IReadOnlyList<string> Files,
    bool Clean,
    IReadOnlyList<string> Signals,
    DateTimeOffset RecordedAt);
