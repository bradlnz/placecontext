using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.DatabaseInspection;

public sealed record ReadDatabaseTableQuery(string Table, int Offset = 0) : IQuery<DatabaseTablePage>;
