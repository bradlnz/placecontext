namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityRecordUpdatePageRequest(
    IReadOnlyDictionary<string, string?> Keys, IReadOnlyDictionary<string, string?> Values);
