namespace PlaceContext.Data.Contracts.Api;

public sealed record InternalInsertRowRequest(
    string TableName,
    IReadOnlyDictionary<string, string?> Values);
