namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchIndexView(string Name, long DocumentCount, string? StoreSize);
