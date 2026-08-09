namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityRecordDeletePageRequest(IReadOnlyDictionary<string, string?> Keys);
