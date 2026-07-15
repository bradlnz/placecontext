using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure;

namespace PlaceContext.Infrastructure.ProjectData;

/// <summary>
/// Each project's own database, realised as a private Postgres schema plus a matching NOLOGIN
/// role. Every execution runs as that role with <c>search_path</c> pinned to the project's schema,
/// so Postgres itself enforces the isolation: the role holds privileges on its own schema only —
/// it cannot see another project's tables or the platform's (which live in <c>public</c>, where
/// this role has no table grants). Provisioning is idempotent and happens lazily on first use.
/// </summary>
public sealed class NpgsqlProjectDataStore : IProjectDataStore
{
    private const int MaxRows = 500;
    private const string StatementTimeout = "10s";

    private readonly string _connectionString;
    private readonly ILogger<NpgsqlProjectDataStore>? _log;

    public NpgsqlProjectDataStore(IConfiguration config, ILogger<NpgsqlProjectDataStore>? log = null)
    {
        _connectionString = config.GetSection("PlaceContext")["ConnectionString"]
            ?? new PlaceContextOptions().ConnectionString;
        _log = log;
    }

    /// <summary>Schema/role name for a project: "proj_" + the guid without dashes (fits PG's 63-char limit).</summary>
    internal static string SchemaFor(Guid projectId) => "proj_" + projectId.ToString("N");

    // DDL can't be parameterised, so identifiers are validated then quote-escaped. A valid identifier
    // is a letter/underscore start, then letters/digits/underscores, ≤ 63 chars (Postgres's limit).
    private static readonly Regex IdentRe = new(@"^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.Compiled);

    // Column types the wizard offers — an allow-list, so a type string can't smuggle in SQL.
    internal static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = "text",
        ["integer"] = "integer",
        ["bigint"] = "bigint",
        ["numeric"] = "numeric",
        ["boolean"] = "boolean",
        ["timestamptz"] = "timestamptz",
        ["date"] = "date",
        ["uuid"] = "uuid",
        ["jsonb"] = "jsonb",
    };

    private static string Ident(string name, string what)
    {
        if (string.IsNullOrWhiteSpace(name) || !IdentRe.IsMatch(name))
            throw new ArgumentException($"Invalid {what} '{name}'. Use letters, digits and underscores (max 63, no leading digit).");
        return name;
    }

    private static string QuoteIdent(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    public async Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
    {
        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);

        // The user's SQL runs inside a transaction as the project's role, with search_path and a
        // statement timeout pinned. SET LOCAL scopes all three to this transaction only.
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var setup = conn.CreateCommand())
        {
            setup.Transaction = tx;
            setup.CommandText =
                $"SET LOCAL ROLE \"{schema}\"; " +
                $"SET LOCAL search_path TO \"{schema}\"; " +
                $"SET LOCAL statement_timeout = '{StatementTimeout}'";
            await setup.ExecuteNonQueryAsync(ct);
        }

        var columns = new List<string>();
        var rows = new List<IReadOnlyList<string?>>();
        var affected = 0;
        var truncated = false;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            do
            {
                if (reader.FieldCount == 0)
                    continue;
                // A later result set replaces an earlier one — "the last SELECT wins".
                columns.Clear();
                rows.Clear();
                truncated = false;
                for (var i = 0; i < reader.FieldCount; i++)
                    columns.Add(reader.GetName(i));
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count >= MaxRows) { truncated = true; break; }
                    var row = new string?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[i] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i).ToString();
                    rows.Add(row);
                }
            }
            while (await reader.NextResultAsync(ct));
            affected = reader.RecordsAffected < 0 ? 0 : reader.RecordsAffected;
        }

        await tx.CommitAsync(ct);
        return new ProjectQueryResult(columns, rows, affected, truncated);
    }

    // ── Pagination math + predicate building, split out as pure static helpers so they're testable
    // without a live Postgres connection (see NpgsqlProjectDataStorePagingTests). ──
    internal const int MaxPageSize = 200;

    internal static int ClampPage(int page) => Math.Max(1, page);

    internal static int ClampPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    internal static long OffsetFor(int page, int pageSize) => (long)(ClampPage(page) - 1) * ClampPageSize(pageSize);

    /// <summary>
    /// The "search every column" WHERE clause (each column already quoted/validated) — null when
    /// there's no search term or the table has no columns. The clause never embeds the search
    /// term itself; callers bind it as the <c>@search</c> parameter.
    /// </summary>
    internal static string? BuildSearchWhereClause(IReadOnlyList<string> columns, string? search)
    {
        if (string.IsNullOrWhiteSpace(search) || columns.Count == 0) return null;
        var predicates = columns.Select(c => $"{QuoteIdent(c)}::text ILIKE @search");
        return " WHERE " + string.Join(" OR ", predicates);
    }

    public async Task<ProjectTablePageResult> QueryTablePageAsync(Guid projectId, string tableName, string? search,
        int page, int pageSize, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        page = ClampPage(page);
        pageSize = ClampPageSize(pageSize);

        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);

        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var setup = conn.CreateCommand())
        {
            setup.Transaction = tx;
            setup.CommandText =
                $"SET LOCAL ROLE \"{schema}\"; " +
                $"SET LOCAL search_path TO \"{schema}\"; " +
                $"SET LOCAL statement_timeout = '{StatementTimeout}'";
            await setup.ExecuteNonQueryAsync(ct);
        }

        var quoted = QuoteIdent(tableName);

        // Column names come straight off the result set's own schema (no untrusted input here),
        // then get re-quoted below — used to build the "search every column" predicate.
        var columns = new List<string>();
        await using (var colCmd = conn.CreateCommand())
        {
            colCmd.Transaction = tx;
            colCmd.CommandText = $"SELECT * FROM {quoted} LIMIT 0";
            await using var colReader = await colCmd.ExecuteReaderAsync(ct);
            for (var i = 0; i < colReader.FieldCount; i++)
                columns.Add(colReader.GetName(i));
        }

        var whereSql = BuildSearchWhereClause(columns, search) ?? "";
        var hasSearch = whereSql.Length > 0;

        long total;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.Transaction = tx;
            countCmd.CommandText = $"SELECT count(*) FROM {quoted}{whereSql}";
            // The search term is always bound as a parameter — never interpolated into the SQL text.
            if (hasSearch) countCmd.Parameters.AddWithValue("search", $"%{search}%");
            total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct) ?? 0L);
        }

        var rows = new List<IReadOnlyList<string?>>();
        var offset = OffsetFor(page, pageSize);
        await using (var pageCmd = conn.CreateCommand())
        {
            pageCmd.Transaction = tx;
            // ORDER BY 1 keeps paging stable/deterministic across requests (the table has no
            // guaranteed primary key visible here — the first column is the best available anchor).
            pageCmd.CommandText = $"SELECT * FROM {quoted}{whereSql} ORDER BY 1 LIMIT @take OFFSET @skip";
            if (hasSearch) pageCmd.Parameters.AddWithValue("search", $"%{search}%");
            pageCmd.Parameters.AddWithValue("take", pageSize);
            pageCmd.Parameters.AddWithValue("skip", offset);
            await using var reader = await pageCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    row[i] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i).ToString();
                rows.Add(row);
            }
        }

        await tx.CommitAsync(ct);
        return new ProjectTablePageResult(columns, rows, total, page, pageSize);
    }

    public async Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
    {
        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);

        // A table the project role does not own is system-written and read-only for the project
        // (the role holds SELECT at most — see AppendReadOnlyRowsAsync).
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.relname, GREATEST(c.reltuples, 0)::bigint,
                   pg_get_userbyid(c.relowner) <> n.nspname,
                   c.relkind = 'v'
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind IN ('r', 'v')
            ORDER BY c.relname
            """;
        cmd.Parameters.AddWithValue("schema", schema);
        var tables = new List<ProjectTableInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            tables.Add(new ProjectTableInfo(reader.GetString(0), reader.GetInt64(1), reader.GetBoolean(2), reader.GetBoolean(3)));
        return tables;
    }

    public async Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        if (columns is null || columns.Count == 0)
            throw new ArgumentException("A table needs at least one column.");

        var defs = new List<string>();
        var pkCols = new List<string>();
        foreach (var c in columns)
        {
            Ident(c.Name, "column name");
            if (!AllowedTypes.TryGetValue(c.Type, out var pgType))
                throw new ArgumentException($"Unsupported column type '{c.Type}'.");
            var def = $"{QuoteIdent(c.Name)} {pgType}";
            if (c.NotNull || c.PrimaryKey) def += " NOT NULL";
            defs.Add(def);
            if (c.PrimaryKey) pkCols.Add(QuoteIdent(c.Name));
        }
        if (pkCols.Count > 0)
            defs.Add($"PRIMARY KEY ({string.Join(", ", pkCols)})");

        // Runs as the project role in its own schema — same isolation as ExecuteAsync.
        await RunAsRoleAsync(projectId,
            $"CREATE TABLE {QuoteIdent(tableName)} ({string.Join(", ", defs)})", ct);
    }

    public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
    {
        Ident(from, "table name");
        Ident(to, "new table name");
        return RunAsRoleAsync(projectId, $"ALTER TABLE {QuoteIdent(from)} RENAME TO {QuoteIdent(to)}", ct);
    }

    public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        return RunAsRoleAsync(projectId, $"DROP TABLE {QuoteIdent(tableName)}", ct);
    }

    public async Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull,
                   COALESCE(i.indisprimary, false)
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_index i ON i.indrelid = c.oid AND i.indisprimary AND a.attnum = ANY(i.indkey)
            WHERE n.nspname = @schema AND c.relname = @table AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """;
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", tableName);
        var columns = new List<ProjectColumnInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(new ProjectColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3)));
        return columns;
    }

    public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        Ident(column.Name, "column name");
        if (!AllowedTypes.TryGetValue(column.Type, out var pgType))
            throw new ArgumentException($"Unsupported column type '{column.Type}'.");
        if (column.PrimaryKey)
            throw new ArgumentException("A primary key can only be set when the table is created.");
        var def = $"{QuoteIdent(column.Name)} {pgType}";
        if (column.NotNull) def += " NOT NULL"; // Postgres rejects this if the table already has rows.
        return RunAsRoleAsync(projectId, $"ALTER TABLE {QuoteIdent(tableName)} ADD COLUMN {def}", ct);
    }

    public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        Ident(columnName, "column name");
        return RunAsRoleAsync(projectId, $"ALTER TABLE {QuoteIdent(tableName)} DROP COLUMN {QuoteIdent(columnName)}", ct);
    }

    public async Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        var result = await ExecuteAsync(projectId, $"SELECT * FROM {QuoteIdent(tableName)}", ct);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", result.Columns.Select(CsvField)));
        foreach (var row in result.Rows)
            sb.AppendLine(string.Join(",", row.Select(v => CsvField(v ?? ""))));
        return sb.ToString();
    }

    private static string CsvField(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    public async Task AppendReadOnlyRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        if (columns is null || columns.Count == 0)
            throw new ArgumentException("A table needs at least one column.");
        var pgTypes = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            Ident(columns[i].Name, "column name");
            if (!AllowedTypes.TryGetValue(columns[i].Type, out var pgType))
                throw new ArgumentException($"Unsupported column type '{columns[i].Type}'.");
            pgTypes[i] = pgType;
        }
        if (rows.Any(r => r.Count != columns.Count))
            throw new ArgumentException("Every row must have exactly one value per column.");

        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);

        // Everything runs as the PLATFORM user, not the project role: the platform owns the table,
        // so the role cannot ALTER, DROP, or (after the revoke) write it — Postgres enforces the
        // read-only contract, the UI merely reflects it.
        await using var tx = await conn.BeginTransactionAsync(ct);

        // A same-named table the project role owns is a user table — refuse rather than hijack it.
        await using (var owner = conn.CreateCommand())
        {
            owner.Transaction = tx;
            owner.CommandText = """
                SELECT pg_get_userbyid(c.relowner)
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table AND c.relkind = 'r'
                """;
            owner.Parameters.AddWithValue("schema", schema);
            owner.Parameters.AddWithValue("table", tableName);
            if (await owner.ExecuteScalarAsync(ct) is string existingOwner && existingOwner == schema)
                throw new InvalidOperationException(
                    $"Table '{tableName}' already exists as a project-owned table — cannot repurpose it as a read-only system table.");
        }

        var qualified = $"\"{schema}\".{QuoteIdent(tableName)}";
        await using (var create = conn.CreateCommand())
        {
            var defs = columns.Select((c, i) => $"{QuoteIdent(c.Name)} {pgTypes[i]}" + (c.NotNull ? " NOT NULL" : ""));
            create.Transaction = tx;
            // The revoke also undoes the schema's ALTER DEFAULT PRIVILEGES auto-grant on this table.
            create.CommandText =
                $"CREATE TABLE IF NOT EXISTS {qualified} ({string.Join(", ", defs)}); " +
                $"REVOKE ALL ON TABLE {qualified} FROM \"{schema}\"; " +
                $"GRANT SELECT ON TABLE {qualified} TO \"{schema}\"";
            await create.ExecuteNonQueryAsync(ct);
        }

        // Values travel as text and are cast to each column's declared type server-side.
        var cols = string.Join(", ", columns.Select(c => QuoteIdent(c.Name)));
        var placeholders = string.Join(", ", columns.Select((_, i) => $"@p{i}::{pgTypes[i]}"));
        foreach (var row in rows)
        {
            await using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = $"INSERT INTO {qualified} ({cols}) VALUES ({placeholders})";
            for (var i = 0; i < columns.Count; i++)
                insert.Parameters.Add(new NpgsqlParameter($"p{i}", NpgsqlTypes.NpgsqlDbType.Text)
                    { Value = (object?)row[i] ?? DBNull.Value });
            await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<int> ImportRowsAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns,
        IReadOnlyList<IReadOnlyList<string?>> rows, bool createTable, CancellationToken ct = default)
    {
        Ident(tableName, "table name");
        if (columns is null || columns.Count == 0)
            throw new ArgumentException("A table needs at least one column.");
        var pgTypes = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            Ident(columns[i].Name, "column name");
            if (!AllowedTypes.TryGetValue(columns[i].Type, out var pgType))
                throw new ArgumentException($"Unsupported column type '{columns[i].Type}'.");
            pgTypes[i] = pgType;
        }
        if (rows.Any(r => r.Count != columns.Count))
            throw new ArgumentException("Every row must have exactly one value per column.");

        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);

        // Runs as the PROJECT ROLE inside its schema, so the created table is project-owned and stays
        // fully writable/alterable/droppable — the CSV import produces a normal user table, not a
        // locked-down system one. Same isolation as ExecuteAsync/CreateTableAsync.
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var setup = conn.CreateCommand())
        {
            setup.Transaction = tx;
            setup.CommandText =
                $"SET LOCAL ROLE \"{schema}\"; SET LOCAL search_path TO \"{schema}\"; " +
                $"SET LOCAL statement_timeout = '{StatementTimeout}'";
            await setup.ExecuteNonQueryAsync(ct);
        }

        if (createTable)
        {
            var defs = columns.Select((c, i) => $"{QuoteIdent(c.Name)} {pgTypes[i]}" + (c.NotNull ? " NOT NULL" : ""));
            await using var create = conn.CreateCommand();
            create.Transaction = tx;
            create.CommandText = $"CREATE TABLE IF NOT EXISTS {QuoteIdent(tableName)} ({string.Join(", ", defs)})";
            await create.ExecuteNonQueryAsync(ct);
        }

        // Values travel as text and are cast to each column's declared type server-side.
        var cols = string.Join(", ", columns.Select(c => QuoteIdent(c.Name)));
        var placeholders = string.Join(", ", columns.Select((_, i) => $"@p{i}::{pgTypes[i]}"));
        var inserted = 0;
        foreach (var row in rows)
        {
            await using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = $"INSERT INTO {QuoteIdent(tableName)} ({cols}) VALUES ({placeholders})";
            for (var i = 0; i < columns.Count; i++)
                insert.Parameters.Add(new NpgsqlParameter($"p{i}", NpgsqlTypes.NpgsqlDbType.Text)
                    { Value = (object?)row[i] ?? DBNull.Value });
            inserted += await insert.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return inserted;
    }

    // Run a single non-query statement inside the project's schema as its role (DDL helper).
    private async Task RunAsRoleAsync(Guid projectId, string statement, CancellationToken ct)
    {
        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var setup = conn.CreateCommand())
        {
            setup.Transaction = tx;
            setup.CommandText =
                $"SET LOCAL ROLE \"{schema}\"; SET LOCAL search_path TO \"{schema}\"; " +
                $"SET LOCAL statement_timeout = '{StatementTimeout}'";
            await setup.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    // Idempotent: schema + NOLOGIN role, role owns full rights on its schema and nothing else.
    // System tables (platform-owned, e.g. job_run_data) are clawed back to SELECT-only afterwards,
    // because the blanket grant would otherwise re-open writes on every provisioning pass.
    private async Task EnsureProvisionedAsync(NpgsqlConnection conn, string schema, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE SCHEMA IF NOT EXISTS "{schema}";
            DO $$ BEGIN CREATE ROLE "{schema}" NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            GRANT USAGE, CREATE ON SCHEMA "{schema}" TO "{schema}";
            GRANT ALL ON ALL TABLES IN SCHEMA "{schema}" TO "{schema}";
            GRANT ALL ON ALL SEQUENCES IN SCHEMA "{schema}" TO "{schema}";
            ALTER DEFAULT PRIVILEGES IN SCHEMA "{schema}" GRANT ALL ON TABLES TO "{schema}";
            ALTER DEFAULT PRIVILEGES IN SCHEMA "{schema}" GRANT ALL ON SEQUENCES TO "{schema}";
            REVOKE ALL ON SCHEMA public FROM "{schema}";
            DO $$ DECLARE t name;
            BEGIN
              FOR t IN SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                       WHERE n.nspname = '{schema}' AND c.relkind = 'r' AND pg_get_userbyid(c.relowner) <> '{schema}'
              LOOP
                EXECUTE format('REVOKE ALL ON TABLE %I.%I FROM %I', '{schema}', t, '{schema}');
                EXECUTE format('GRANT SELECT ON TABLE %I.%I TO %I', '{schema}', t, '{schema}');
              END LOOP;
            END $$;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
