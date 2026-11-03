namespace PlaceContext.Application.ProjectData;

/// <summary>
/// One page of a project table's rows, optionally filtered by a case-insensitive search across
/// every column (each cast to text). <see cref="TotalCount"/> is the count over the WHERE clause
/// when TotalCountIsExact is true; otherwise it is a lower bound from the page plus one lookahead row.
/// </summary>
public sealed record ProjectTablePageResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long TotalCount,
    int Page,
    int PageSize,
    bool TotalCountIsExact = true);
