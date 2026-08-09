namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityRecordLinksPageRequest(IReadOnlyDictionary<string, string?> Values);
