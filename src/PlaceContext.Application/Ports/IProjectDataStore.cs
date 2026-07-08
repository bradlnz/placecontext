namespace PlaceContext.Application.Ports;

/// <summary>One table in a project's own database.</summary>
public sealed record ProjectTableInfo(string Name, long RowEstimate);

/// <summary>
/// The outcome of one SQL execution against a project's database: the last result set (if any),
/// rows affected by writes, and whether the result was cut at the row cap.
/// </summary>
public sealed record ProjectQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int AffectedRows,
    bool Truncated);

/// <summary>
/// Each project's own database: a private, isolated namespace of tables the project can create,
/// fill, and query with SQL. Isolation is the store's job — a project's SQL must never be able to
/// see another project's tables or the platform's own.
/// </summary>
public interface IProjectDataStore
{
    /// <summary>Execute SQL (DDL or DML — multiple statements allowed) inside the project's database.</summary>
    Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default);

    /// <summary>The project's tables with an approximate row count, name-sorted.</summary>
    Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default);
}
