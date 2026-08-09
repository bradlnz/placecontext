namespace PlaceContext.Data.Contracts.Api;

public sealed record ProjectDataRowLinksRequest(
    string TableName,
    IReadOnlyDictionary<string, string?> Values);
