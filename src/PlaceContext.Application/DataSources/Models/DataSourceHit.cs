namespace PlaceContext.Application.DataSources;

public sealed record DataSourceHit(string Citation, Guid SourceId, string SourceName, string Table,
    string RecordKey, int Chunk, string Text, IReadOnlyList<string> Tags, double Score, DateTimeOffset UpdatedAt);
