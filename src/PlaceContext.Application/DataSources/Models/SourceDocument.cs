namespace PlaceContext.Application.DataSources;

public sealed record SourceDocument(string Key, string Json, string Fingerprint,
    IReadOnlyList<SourceChunk> Chunks, DateTimeOffset UpdatedAt, SourceReview? Review = null, string? ValidationError = null);
