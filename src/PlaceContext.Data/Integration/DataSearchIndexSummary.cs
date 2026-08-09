using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Integration;

public sealed record DataSearchIndexSummary(string Name, long DocumentCount, string? StoreSize);
