using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using PlaceContext.Application.Workload;
using StackExchange.Redis;
using k8s;

namespace PlaceContext.Infrastructure.Workload;

public sealed partial class ClusterServiceConsole
{
    public async Task<DatabaseQueryResult> QueryAsync(Guid projectId, Guid databaseId, string query, string? cursor = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 32768)
        {
            throw new ArgumentException("Enter a query of at most 32,768 characters.");
        }
        var database = await OwnedAsync(projectId, databaseId, "database", ct);
        if (!Guid.TryParse(Value(database.Metadata.Labels, Owner), out var appId))
        {
            throw new InvalidOperationException("This database has no assigned app.");
        }
        await OwnedAsync(projectId, appId, "service", ct);
        var secret = await client.CoreV1.ReadNamespacedSecretAsync("credentials", database.Metadata.Name, cancellationToken: ct);
        var postgres = database.Metadata.Labels[Engine] == "postgres";
        var password = Encoding.UTF8.GetString(secret.Data[postgres ? "POSTGRES_PASSWORD" : "APP_PASSWORD"]);
        var address = await ServiceAddressAsync("database", database.Metadata.Name, ct);
        return postgres ? await QueryPostgresAsync(address, password, query, ct) : await QueryRedisAsync(address, password, query, cursor, ct);
    }

    private static async Task<DatabaseQueryResult> QueryPostgresAsync(string address, string password, string query, CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = address,
            Port = 5432,
            Database = "app",
            Username = "postgres",
            Password = password,
            Timeout = 5,
            CommandTimeout = 5,
            Pooling = false,
            IncludeErrorDetail = false
        };
        var readerPassword = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("pc-studio:" + password)));
        await using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync(ct);
            // A separate login enforces read-only grants even if submitted SQL ends its transaction.
            await using var setup = new NpgsqlCommand($"""
                DO $setup$ BEGIN
                  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pc_studio') THEN CREATE ROLE pc_studio LOGIN; END IF;
                END $setup$;
                ALTER ROLE pc_studio LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{readerPassword}';
                GRANT pg_read_all_data TO pc_studio;
                ALTER ROLE pc_studio SET default_transaction_read_only = on;
                ALTER ROLE pc_studio SET statement_timeout = '5s';
                """, admin);
            await setup.ExecuteNonQueryAsync(ct);
        }
        builder.Username = "pc_studio";
        builder.Password = readerPassword;
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(query, connection) { CommandTimeout = 5, AllResultTypesAreUnknown = true };
        await using var reader = await command.ExecuteReaderAsync(ct);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyList<string?>>();
        var chars = 0;
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= 500 || chars >= 1048576)
            {
                return new(columns, rows, true);
            }
            var row = new string?[columns.Length];
            for (var index = 0; index < row.Length; index++)
            {
                var value = await reader.IsDBNullAsync(index, ct) ? null : reader.GetString(index);
                row[index] = value?.Length > 16384 ? value[..16384] + "…" : value;
                chars += row[index]?.Length ?? 0;
            }
            rows.Add(row);
        }
        return new(columns, rows);
    }

    internal static (string Command, string Argument) RedisReadCommand(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Enter a Redis read command.");
        }
        var parts = query.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToUpperInvariant();
        var argument = parts.Length == 2 ? parts[1].Trim() : "";
        if (command is not ("SCAN" or "GET" or "TYPE" or "TTL") || (command != "SCAN" && argument.Length == 0))
        {
            throw new ArgumentException("Use SCAN [pattern], GET key, TYPE key, or TTL key. Writes and arbitrary commands are disabled.");
        }
        return (command, argument);
    }

    private static async Task<DatabaseQueryResult> QueryRedisAsync(string address, string password, string query, string? cursor, CancellationToken ct)
    {
        var (command, argument) = RedisReadCommand(query);
        var config = new ConfigurationOptions { Password = password, AbortOnConnectFail = true, ConnectTimeout = 5000, AsyncTimeout = 5000 };
        config.EndPoints.Add(address, 6379);
        using var connection = await ConnectionMultiplexer.ConnectAsync(config).WaitAsync(ct);
        var db = connection.GetDatabase();
        if (command == "SCAN")
        {
            if (!ulong.TryParse(cursor ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out var position))
            {
                throw new ArgumentException("Invalid scan cursor.");
            }
            var result = (RedisResult[])(await db.ExecuteAsync("SCAN", position.ToString(CultureInfo.InvariantCulture), "MATCH", argument.Length == 0 ? "*" : argument, "COUNT", 100).WaitAsync(ct))!;
            var next = result[0].ToString();
            var keys = (RedisResult[])result[1]!;
            return new(["Key"], keys.Take(500).Select(key => (IReadOnlyList<string?>)new string?[] { key.ToString() }).ToList(), keys.Length > 500, next == "0" ? null : next);
        }
        // GETRANGE bounds large values without fetching the whole object into the portal process.
        var value = command == "GET" ? await db.ExecuteAsync("GETRANGE", argument, 0, 16383).WaitAsync(ct)
            : await db.ExecuteAsync(command, argument).WaitAsync(ct);
        return new(["Key", command], [new string?[] { argument, value.IsNull ? null : value.ToString() }]);
    }
}
