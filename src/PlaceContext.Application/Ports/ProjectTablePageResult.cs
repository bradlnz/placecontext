namespace PlaceContext.Application.Ports;

/// <summary>
/// One page of a project table's rows, optionally filtered by a case-insensitive search across
/// every column (each cast to text). <see cref="TotalCount"/> is the count over the WHERE clause
/// (i.e. matching the search, not the whole table) so "showing X–Y of Z" is always accurate.
/// </summary>
public sealed record ProjectTablePageResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long TotalCount,
    int Page,
    int PageSize);
