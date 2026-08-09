namespace PlaceContext.Application.Ports;

/// <summary>A past commit read from a repository's history — raw material for ledger backfill.</summary>
public sealed record CommitInfo(string Sha, string AuthorName, string Message, DateTimeOffset Date, IReadOnlyList<string> Files);
