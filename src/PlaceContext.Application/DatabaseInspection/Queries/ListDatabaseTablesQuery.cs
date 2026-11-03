using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.DatabaseInspection;

public sealed record ListDatabaseTablesQuery : IQuery<IReadOnlyList<DatabaseTable>>;
