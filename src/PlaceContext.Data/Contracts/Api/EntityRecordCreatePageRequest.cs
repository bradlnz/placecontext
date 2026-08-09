namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityRecordCreatePageRequest(IReadOnlyDictionary<string, string?> Values);
