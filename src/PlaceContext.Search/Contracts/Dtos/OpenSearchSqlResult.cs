namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchSqlResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int AffectedRows,
    bool Truncated);
