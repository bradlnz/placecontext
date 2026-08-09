namespace PlaceContext.Data.Contracts.Api;

public sealed record EntityRecordsResponse(
    Guid EntityId, string EntityName, string TableName, IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows, long Total, int Page, int PageSize);
