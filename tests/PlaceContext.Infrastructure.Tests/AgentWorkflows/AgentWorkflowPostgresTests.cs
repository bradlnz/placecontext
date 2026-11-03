using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.AgentWorkflows;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

public sealed class AgentWorkflowPostgresTests
{
    [AgentPostgresFact]
    public async Task Migration_preserves_legacy_tables_and_concurrent_retries_have_one_execution_and_usage_record()
    {
        var schema = "agent_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            await using var fixture = new AgentWorkflowFixture(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
            await using (var db = fixture.Db())
            {
                await db.Database.MigrateAsync();
            }
            await using (var legacy = new NpgsqlCommand("SELECT to_regclass('agent_definitions') IS NOT NULL", connection))
            {
                Assert.True((bool)(await legacy.ExecuteScalarAsync())!);
            }
            await fixture.SeedAsync();
            var service = fixture.Service();
            await service.SetSubscriptionAsync(fixture.ProjectId, "services", true);
            var id = Guid.NewGuid();
            const string input = """{"client":"Client","scope":"Scope","documents":"Documents"}""";
            var services = Enumerable.Range(0, 12).Select(_ => fixture.Service()).ToArray();
            var results = await Task.WhenAll(services.Select(s => s.RunAsync(fixture.ProjectId, "services", id, input)));
            Assert.Equal(1, fixture.Model.Calls);
            Assert.All(results, run => Assert.Equal(id, run.Id));
            await using var check = fixture.Db();
            var stored = await check.AgentRuns.SingleAsync();
            Assert.Equal(AgentRunStatus.Completed, stored.Status);
            Assert.Equal(1.25m, stored.DemoCredits);
            Assert.Single(await check.UsageRecords.ToListAsync());
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
