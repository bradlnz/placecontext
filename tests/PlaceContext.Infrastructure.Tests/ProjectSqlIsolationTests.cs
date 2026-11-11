using Microsoft.Extensions.Configuration;
using Npgsql;
using PlaceContext.Application.ProjectData;
using PlaceContext.Infrastructure.ProjectData;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ProjectSqlIsolationTests
{
    [ChainInputResumeTests.PostgresFact]
    public async Task Project_connections_cannot_reset_into_platform_or_other_project_privileges()
    {
        var cs = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")!;
        var platformRole = new NpgsqlConnectionStringBuilder(cs).Username!.Replace("\"", "\"\"");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PlaceContext:ConnectionString"] = cs }).Build();
        var store = new SqlProjectDataStore(config);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var schemaA = SqlProjectDataStore.SchemaFor(a);
        var schemaB = SqlProjectDataStore.SchemaFor(b);
        await using var owner = new NpgsqlConnection(cs);
        await owner.OpenAsync();
        // Upgrade an existing NOLOGIN project role without changing ownership or deleting its data.
        await new NpgsqlCommand($"""
            CREATE ROLE "{schemaA}" NOLOGIN;
            CREATE SCHEMA "{schemaA}";
            GRANT USAGE, CREATE ON SCHEMA "{schemaA}" TO "{schemaA}";
            SET ROLE "{schemaA}";
            CREATE TABLE "{schemaA}".records (id integer);
            INSERT INTO "{schemaA}".records VALUES (7);
            RESET ROLE;
            """, owner).ExecuteNonQueryAsync();
        try
        {
            var first = store.ExecuteAsync(a, "RESET ROLE; SELECT session_user, current_user");
            var second = store.ExecuteAsync(b, "CREATE TABLE private_records (value text); INSERT INTO private_records VALUES ('synthetic')");
            await Task.WhenAll(first, second);
            var identity = await first;
            Assert.Equal(schemaA, identity.Rows[0][0]);
            Assert.Equal(schemaA, identity.Rows[0][1]);
            Assert.Equal("7", (await store.QueryAsync(a, "SELECT id FROM records")).Rows[0][0]);
            foreach (var sql in new[] {
                $"RESET ROLE; SELECT * FROM \"{schemaB}\".private_records",
                $"SET ROLE \"{platformRole}\"; SELECT current_user",
                "RESET SESSION AUTHORIZATION; SELECT * FROM pg_authid",
                "COMMIT; RESET ROLE; SELECT * FROM pg_authid"
            })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => store.ExecuteAsync(a, sql));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            var escape = $"SELECT query_to_xml('SELECT * FROM \"{schemaB}\".private_records', false, true, '') FROM (SELECT set_config('role', 'none', true)) AS bypass";
            var denied = await Assert.ThrowsAsync<PostgresException>(() => store.QueryAsync(a, escape));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            await Assert.ThrowsAsync<PostgresException>(() => store.QueryAsync(a, "SELECT 1); COMMIT; DELETE FROM records; SELECT 1 --"));
            Assert.Equal("7", (await store.QueryAsync(a, "SELECT id FROM records")).Rows[0][0]);
            await store.ExecuteAsync(a, "INSERT INTO records VALUES (8)");
            Assert.Equal(2, (await store.QueryAsync(a, "SELECT id FROM records ORDER BY id")).Rows.Count);
            await Task.WhenAll(Enumerable.Range(9, 2).Select(id => store.AppendReadOnlyRowsAsync(a,
                "system_records", [new ProjectColumnSpec("id", "integer", true, false)], [[id.ToString()]])));
            Assert.Equal(2, (await store.QueryAsync(a, "SELECT id FROM system_records")).Rows.Count);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendReadOnlyRowsAsync(a,
                "records", [new ProjectColumnSpec("id", "integer", true, false)], [["9"]]));
            await Assert.ThrowsAsync<PostgresException>(() => store.ExecuteAsync(a, "DELETE FROM system_records"));
            Assert.Equal("synthetic", (await store.QueryAsync(b, "SELECT value FROM private_records")).Rows[0][0]);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            foreach (var schema in new[] { schemaA, schemaB })
            {
                await new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", owner).ExecuteNonQueryAsync();
                if ((bool)(await new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{schema}')", owner).ExecuteScalarAsync())!)
                    await new NpgsqlCommand($"DROP OWNED BY \"{schema}\"; DROP ROLE \"{schema}\"", owner).ExecuteNonQueryAsync();
            }
        }
    }
}
