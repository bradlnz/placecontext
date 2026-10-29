using System.Text;
using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>
/// The materialization outcome: the created index, how many rows were copied, and whether the
/// table held more rows than the export cap (rows beyond it were not indexed).
/// </summary>
public sealed record MaterializeTableIndexResult(
    string IndexName,
    long RowsIndexed,
    int ColumnCount,
    bool Truncated,
    string SourceTable);
