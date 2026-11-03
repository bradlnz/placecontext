namespace PlaceContext.Application.DataSources;

public sealed record DataSourceSearchResult(string Mode, IReadOnlyList<DataSourceHit> Hits);
