namespace PlaceContext.Application.DataSources;

public sealed record SourceDataset(string Schema, string Table, string KeyColumn, IReadOnlyList<string> Columns);
