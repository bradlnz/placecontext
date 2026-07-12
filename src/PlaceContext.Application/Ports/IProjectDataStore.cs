namespace PlaceContext.Application.Ports;

/// <summary>One table in a project's own database. Read-only tables are system-written (e.g.
/// job run results): the project can SELECT them but not modify, rename, or drop them.</summary>
public sealed record ProjectTableInfo(string Name, long RowEstimate, bool ReadOnly = false, bool IsView = false);

/// <summary>One column in a create-table request. Type is a Postgres type chosen from a safe allow-list.</summary>
public sealed record ProjectColumnSpec(string Name, string Type, bool NotNull, bool PrimaryKey);

/// <summary>One existing column of a project table, as the database reports it.</summary>
public sealed record ProjectColumnInfo(string Name, string Type, bool NotNull, bool PrimaryKey);

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

    /// <summary>Create a table from a validated spec (identifiers and types are checked, then quoted).</summary>
    Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default);

    /// <summary>Rename a table within the project's schema.</summary>
    Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default);

    /// <summary>The columns of one table, in table order.</summary>
    Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default);

    /// <summary>Add a column to an existing table (identifier and type are checked, then quoted).</summary>
    Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default);

    /// <summary>Drop a column from an existing table.</summary>
    Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default);

    /// <summary>Drop a table from the project's schema.</summary>
    Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default);

    /// <summary>Export a whole table as CSV (row-capped for safety); returns the CSV text.</summary>
    Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default);

    /// <summary>
    /// Append rows to a system-owned, read-only table in the project's database, creating the table
    /// from the spec on first use. The project can SELECT the table but never write, alter, or drop
    /// it — the store must enforce that, not just the UI. Row values are text; each is cast to its
    /// column's declared type on insert (null stays null).
    /// </summary>
    Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default);
}
