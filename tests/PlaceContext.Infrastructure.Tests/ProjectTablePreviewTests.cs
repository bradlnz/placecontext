using Microsoft.Extensions.Configuration;
using Npgsql;
using PlaceContext.Application.ProjectData;
using PlaceContext.Infrastructure.ProjectData;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ProjectTablePreviewTests
{
    [ChainInputResumeTests.PostgresFact]
    public async Task Paging_without_a_total_preserves_lookahead_last_page_and_explicit_counts()
    {
        var schema = "preview_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var setup = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE records (id integer PRIMARY KEY, "Display Name" text);
            INSERT INTO records SELECT i, 'Record ' || i FROM generate_series(1, 52) i;
            CREATE VIEW names AS SELECT "Display Name" FROM records;
            """, connection);
        await setup.ExecuteNonQueryAsync();
        try
        {
            var store = new SqlProjectDataStore(new ConfigurationBuilder().Build(), new ExternalDatabase(cs));
            var project = Guid.NewGuid();
            var schemas = await store.ListTableSchemasAsync(project);
            Assert.Equal(2, schemas.Count);
            var columns = schemas.Single(table => table.Name == "records").Columns;
            Assert.Equal(["id", "Display Name"], columns.Select(column => column.Name));
            Assert.True(columns[0].PrimaryKey);
            Assert.Single(schemas.Single(table => table.Name == "names").Columns);

            var first = await store.QueryTablePageAsync(project, "records", null, 1, 50, includeTotalCount: false);
            Assert.Equal(50, first.Rows.Count);
            Assert.Equal(51, first.TotalCount);
            Assert.False(first.TotalCountIsExact);
            Assert.Equal("50", first.Rows[^1][0]);
            var last = await store.QueryTablePageAsync(project, "records", null, 2, 50, includeTotalCount: false);
            Assert.Equal(2, last.Rows.Count);
            Assert.Equal(52, last.TotalCount);
            Assert.True(last.TotalCountIsExact);
            var exact = await store.QueryTablePageAsync(project, "records", null, 1, 50);
            Assert.Equal(52, exact.TotalCount);
            Assert.True(exact.TotalCountIsExact);
            var search = await store.QueryTablePageAsync(project, "records", "Record 52", 1, 50, includeTotalCount: false);
            Assert.Single(search.Rows);
            Assert.Equal(1, search.TotalCount);
            Assert.True(search.TotalCountIsExact);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class ExternalDatabase(string connectionString) : IProjectDatabaseConnectionResolver
    {
        public Task<ProjectDatabaseConnection> ResolveAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(new ProjectDatabaseConnection(connectionString, IsExternal: true));
    }
}
