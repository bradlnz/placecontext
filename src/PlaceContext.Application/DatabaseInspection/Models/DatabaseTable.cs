namespace PlaceContext.Application.DatabaseInspection;

public sealed record DatabaseTable(string Name, IReadOnlyList<DatabaseColumn> Columns, bool CanReadRows);
