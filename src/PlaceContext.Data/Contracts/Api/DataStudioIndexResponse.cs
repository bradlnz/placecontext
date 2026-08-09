namespace PlaceContext.Data.Contracts.Api;

/// <summary>Data Studio's caller-local wire view of a search index.</summary>
public sealed record DataStudioIndexResponse(string Name, long DocumentCount, string? StoreSize);
