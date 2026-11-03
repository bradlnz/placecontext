namespace PlaceContext.Application.Dtos;

public sealed record SearchIndexView(string Name, long DocumentCount, string? StoreSize);
