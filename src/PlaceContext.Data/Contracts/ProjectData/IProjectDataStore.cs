namespace PlaceContext.Application.Ports;

/// <summary>
/// Each project's own database: a private, isolated namespace of tables the project can create,
/// fill, and query with SQL. Isolation is the store's job — a project's SQL must never be able to
/// see another project's tables or the platform's own.
/// </summary>
public interface IProjectDataStore
{
    /// <summary>Execute SQL (DDL or DML — multiple statements allowed) inside the project's database.</summary>
    Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default);

    /// <summary>
    /// A server-side paginated, searchable page of one table's rows — the entity Records tab.
    /// <paramref name="search"/> (when non-empty) is bound as a query parameter and matched
    /// case-insensitively against every column cast to text; it is never concatenated into SQL.
    /// <paramref name="sortColumn"/> must be one of the table's actual columns — anything else
    /// falls back to the default first-column ordering (it is never interpolated into SQL).
    /// </summary>
    Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
        int page, int pageSize, string? sortColumn = null, bool sortDescending = false, CancellationToken ct = default);

    /// <summary>
    /// Read every row of a table (row-capped for safety) for external tooling — the OpenSearch
    /// materialisation path. Column order is the table's own; date columns arrive as ISO-8601 UTC.
    /// </summary>
    Task<ProjectTableReadResult> ReadTableAsync(
        Guid projectId, string tableName, long maxRows = 10000, CancellationToken ct = default);

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
    /// from the spec on first use and ADDING any spec column an existing table lacks (flattened
    /// data-map leaves appear as payloads evolve; new columns are added nullable). The project can
    /// SELECT the table but never write, alter, or drop it — the store must enforce that, not just
    /// the UI. Row values are text; each is cast to its column's declared type on insert (null stays null).
    /// </summary>
    Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default);

    /// <summary>
    /// Bulk-insert text rows into a project-OWNED (writable) table — the CSV-import path. When
    /// <paramref name="createTable"/> is true the table is created from the spec on first use; the
    /// table is owned by the project role, so the project can keep editing it (unlike a read-only
    /// system table). Row values are text, each cast to its column's declared type on insert.
    /// Returns the number of rows inserted.
    /// </summary>
    Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default);

    /// <summary>
    /// Insert one project-owned row (CRM path). Column names validated; text/jsonb cells encrypted.
    /// </summary>
    Task InsertRowAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);

    /// <summary>
    /// Update rows matching <paramref name="keys"/> (column → value). At least one key required.
    /// </summary>
    Task<int> UpdateRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys,
        IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);

    /// <summary>Delete rows matching <paramref name="keys"/>. At least one key required.</summary>
    Task<int> DeleteRowsAsync(Guid projectId, string tableName, IReadOnlyDictionary<string, string?> keys, CancellationToken ct = default);
}
