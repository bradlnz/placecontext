namespace PlaceContext.Application.DatabaseInspection;

public sealed record DatabaseSelectStatement(string Table, IReadOnlyList<string> Columns, bool Count,
    IReadOnlyList<DatabaseFilter> Filters, IReadOnlyList<DatabaseSort> Order, int Limit, int Offset);
