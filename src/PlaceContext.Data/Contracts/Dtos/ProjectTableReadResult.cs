namespace PlaceContext.Application.Ports;

/// <summary>
/// A whole-table read for external tooling (e.g. materialising a table into OpenSearch).
/// <see cref="ColumnTypes"/> holds each column's Postgres type name (parallel to <see cref="Columns"/>);
/// date/timestamp columns are already emitted as ISO-8601 (UTC) so they index cleanly, and
/// everything else is its text form. <see cref="Truncated"/> is true when the table holds more
/// rows than the cap that was requested.
/// </summary>
public sealed record ProjectTableReadResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> ColumnTypes,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long TotalCount,
    bool Truncated);
