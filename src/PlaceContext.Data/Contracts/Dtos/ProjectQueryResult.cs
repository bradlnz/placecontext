namespace PlaceContext.Application.Ports;

/// <summary>
/// The outcome of one SQL execution against a project's database: the last result set (if any),
/// rows affected by writes, and whether the result was cut at the row cap.
/// </summary>
public sealed record ProjectQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int AffectedRows,
    bool Truncated);
