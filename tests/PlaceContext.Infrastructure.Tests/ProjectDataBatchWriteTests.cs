using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Secrets;
using PlaceContext.Infrastructure.ProjectData;
using PlaceContext.Infrastructure.Security;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ProjectDataBatchWriteTests
{
    [ChainInputResumeTests.PostgresFact]
    public Task Imports_preserve_types_and_roll_back_across_batch_boundaries() => CheckWritesAsync(false);

    [ChainInputResumeTests.PostgresFact]
    public Task Mapped_outputs_preserve_types_and_roll_back_across_batch_boundaries() => CheckWritesAsync(true);

    private static async Task CheckWritesAsync(bool readOnly)
    {
        var project = Guid.NewGuid();
        var schema = SqlProjectDataStore.SchemaFor(project);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:ConnectionString"] = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")
        }).Build();
        var store = new SqlProjectDataStore(config);
        await using var connection = new NpgsqlConnection(config["PlaceContext:ConnectionString"]);
        await connection.OpenAsync();
        ProjectColumnSpec[] columns =
        [
            new("id", "integer", true, false), new("text", "text", false, false),
            new("payload", "jsonb", false, false), new("amount", "numeric", false, false),
            new("enabled", "boolean", false, false), new("at", "timestamptz", false, false)
        ];
        IReadOnlyList<string?>[] rows = Enumerable.Range(1, 501)
            .Select(i => (IReadOnlyList<string?>)new string?[]
            {
                i.ToString(), i % 2 == 0 ? null : "quote ' slash \\ newline\n雪",
                "{\"value\":1}", "123.45", "true", "2026-11-04T00:00:00Z"
            }).ToArray();
        async Task WriteAsync(IReadOnlyList<IReadOnlyList<string?>> values)
        {
            if (readOnly)
            {
                await store.AppendReadOnlyRowsAsync(project, "batch_records", columns, values);
                return;
            }
            Assert.Equal(values.Count, await store.ImportRowsAsync(project, "batch_records", columns, values, true));
        }
        try
        {
            await WriteAsync(rows);
            await WriteAsync([]);
            await using (var check = new NpgsqlCommand($"""
                SELECT count(*), count(DISTINCT id), count(*) FILTER (WHERE text IS NULL),
                  bool_and(payload = jsonb_build_object('value', 1) AND amount = 123.45 AND enabled
                    AND at = '2026-11-04T00:00:00Z'::timestamptz)
                FROM "{schema}".batch_records
                """, connection))
            {
                await using var reader = await check.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(501, reader.GetInt64(0));
                Assert.Equal(501, reader.GetInt64(1));
                Assert.Equal(250, reader.GetInt64(2));
                Assert.True(reader.GetBoolean(3));
            }
            var invalid = rows.Select(row => row.ToArray()).ToArray();
            invalid[^1][0] = "invalid integer";
            await Assert.ThrowsAsync<PostgresException>(() => WriteAsync(invalid));
            await using (var count = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".batch_records", connection))
            {
                Assert.Equal(501L, await count.ExecuteScalarAsync());
            }
            if (readOnly)
            {
                await Assert.ThrowsAsync<PostgresException>(() => store.InsertRowAsync(project, "batch_records",
                    new Dictionary<string, string?> { ["id"] = "900" }));
                await Task.WhenAll(WriteAsync(rows), WriteAsync(rows));
                await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".batch_records", connection);
                Assert.Equal(1503L, await count.ExecuteScalarAsync());
            }

            var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            config["PlaceContext:EncryptProjectDataAtRest"] = "true";
            var encrypted = new SqlProjectDataStore(config, enc: encryptor);
            await encrypted.ImportRowsAsync(project, "encrypted_records", [new("value", "text", false, false)],
                [new string?[] { "private value" }, new string?[] { null }, new string?[] { "" }], true);
            await using var value = new NpgsqlCommand($"SELECT value FROM \"{schema}\".encrypted_records WHERE value <> ''", connection);
            var ciphertext = (string)(await value.ExecuteScalarAsync())!;
            Assert.True(encryptor.IsProtected(ciphertext));
            Assert.Equal("private value", encryptor.Unprotect(ciphertext, IDataEncryptor.Purpose.ProjectData));
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"""
                DROP SCHEMA IF EXISTS "{schema}" CASCADE;
                DROP OWNED BY "{schema}";
                DROP ROLE "{schema}";
                """, connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
