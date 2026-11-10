using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ChainRunCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_worker_cannot_replace_cancellation(bool trackedBeforeCancellation)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await AssertCancellationWinsAsync(options, trackedBeforeCancellation);
    }

    [ChainInputResumeTests.PostgresFact]
    public async Task Cancellation_survives_both_race_orderings_in_postgres()
    {
        var schema = "cancel_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
        {
            SearchPath = schema
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA {schema};
            CREATE TABLE chain_runs (
              "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ChainId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
              "ChainName" text NOT NULL, "Status" text NOT NULL, "StepsJson" text NOT NULL, "FinalOutput" text,
              "StartedAt" timestamptz NOT NULL, "FinishedAt" timestamptz, "ResumeAt" timestamptz,
              "ResumeStageIndex" integer, "ContinuationClaimedBy" text, "ContinuationClaimedAt" timestamptz,
              "ContinuationOverrides" text, "InputRequest" text, "InitiatedBy" uuid);
            """, connection))
        {
            await setup.ExecuteNonQueryAsync();
        }
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            await AssertCancellationWinsAsync(options, false);
            await AssertCancellationWinsAsync(options, true);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task AssertCancellationWinsAsync(DbContextOptions<AppDbContext> options, bool trackedBeforeCancellation)
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var encryption = new FakeDataEncryptor();
        var now = DateTimeOffset.UtcNow;
        var chain = JobChain.Create(Guid.NewGuid(), "cancel race", null, new[] { Guid.NewGuid() }, now);
        var run = ChainRun.Start(chain, new[] { "work" }, now);
        run.MarkStepRunning(0, Guid.NewGuid(), now);
        run.SetCurrentPayload("preserved checkpoint");
        await using (var seed = new AppDbContext(options, tenant))
        {
            await new EfChainRunRepository(seed, encryption).AddAsync(run);
            await seed.SaveChangesAsync();
        }
        await using var worker = new AppDbContext(options, tenant);
        var workerRuns = new EfChainRunRepository(worker, encryption);
        var stale = (await workerRuns.GetByIdAsync(run.Id))!;
        if (trackedBeforeCancellation)
        {
            await workerRuns.UpdateAsync(stale);
            await worker.SaveChangesAsync();
        }
        await using var canceller = new AppDbContext(options, tenant);
        var cancelledRuns = new EfChainRunRepository(canceller, encryption);
        var cancelled = (await cancelledRuns.GetByIdAsync(run.Id))!;
        cancelled.MarkStepFinished(0, cancelled.Steps[0].RunId, ChainStepStatus.Cancelled, now);
        cancelled.Complete(ChainRunStatus.Cancelled, cancelled.FinalOutput, now);
        await cancelledRuns.UpdateAsync(cancelled);
        await canceller.SaveChangesAsync();

        stale.MarkStepFinished(0, stale.Steps[0].RunId, ChainStepStatus.Succeeded, now);
        stale.Complete(ChainRunStatus.Succeeded, "late output", now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await workerRuns.UpdateAsync(stale);
            await worker.SaveChangesAsync();
        });
        var saved = (await cancelledRuns.GetByIdAsync(run.Id))!;
        Assert.Equal(ChainRunStatus.Cancelled, saved.Status);
        Assert.Equal(ChainStepStatus.Cancelled, saved.Steps[0].Status);
        Assert.Equal("preserved checkpoint", saved.FinalOutput);
    }
}
