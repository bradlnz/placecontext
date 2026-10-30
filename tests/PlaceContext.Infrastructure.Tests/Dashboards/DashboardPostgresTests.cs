using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.Dashboards;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

public sealed class DashboardPostgresTests
{
    [AgentPostgresFact]
    public async Task Migration_and_concurrent_saves_preserve_the_winning_dashboard()
    {
        var schema = "dashboard_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        try
        {
            await using var f = new DashboardFixture(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
            await using (var db = f.Db()) await db.Database.MigrateAsync();
            var chart = await f.SeedAsync();
            var initial = await f.Service().SaveAsync(f.Project, new(Guid.Empty, "Initial", [chart.Id], "Is A larger?", Guid.Empty));
            async Task<PersonalDashboard?> Save(string name)
            {
                try { return await f.Service().SaveAsync(f.Project, initial with { Name = name }); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("another tab")) { return null; }
            }
            var results = await Task.WhenAll(Save("First"), Save("Second"));
            var winner = Assert.Single(results.OfType<PersonalDashboard>());
            Assert.Equal(winner.Name, Assert.Single(await f.Service().ListAsync(f.Project)).Name);
            Assert.Single(await f.Service().ChartsAsync(f.Project));
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection); await cleanup.ExecuteNonQueryAsync();
        }
    }
}
