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

    public async Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
    {
        var schema = SchemaFor(projectId);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await EnsureProvisionedAsync(conn, schema, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.relname, GREATEST(c.reltuples, 0)::bigint
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind = 'r'
            ORDER BY c.relname
            """;
        cmd.Parameters.AddWithValue("schema", schema);
        var tables = new List<ProjectTableInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            tables.Add(new ProjectTableInfo(reader.GetString(0), reader.GetInt64(1)));
        return tables;
    }

    // Idempotent: schema + NOLOGIN role, role owns full rights on its schema and nothing else.
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
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
