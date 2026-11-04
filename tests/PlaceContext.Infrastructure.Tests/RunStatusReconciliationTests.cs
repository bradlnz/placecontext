using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlaceContext.Application.Secrets;
using PlaceContext.Infrastructure.Tenancy;
using Npgsql;
using PlaceContext.Application.Features;
using PlaceContext.Application.Jobs;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.Infrastructure.Security;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class RunStatusReconciliationTests
{
    [ChainInputResumeTests.PostgresFact]
    public async Task Watcher_repairs_abandoned_encrypted_steps_and_notifies_once_without_touching_active_or_other_tenant_runs()
    {
        var schema = "reconcile_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA {schema};
            CREATE TABLE chain_runs (
              "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ChainId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
              "ChainName" text NOT NULL, "Status" text NOT NULL, "StepsJson" text NOT NULL, "FinalOutput" text,
              "StartedAt" timestamptz NOT NULL, "FinishedAt" timestamptz, "ResumeAt" timestamptz,
              "ResumeStageIndex" integer, "ContinuationClaimedBy" text, "ContinuationClaimedAt" timestamptz,
              "ContinuationOverrides" text, "InputRequest" text);
            CREATE TABLE job_runs ("Id" uuid PRIMARY KEY,"TenantId" uuid,"JobId" uuid,"ProjectId" uuid,"Status" text,
              "OriginalRunId" uuid,"AttemptNumber" int,"StartedAt" timestamptz,"FinishedAt" timestamptz);
            CREATE TABLE jobs ("Id" uuid PRIMARY KEY,"TenantId" uuid,"Name" text,"RetryDelaySeconds" int);
            CREATE TABLE rpc_chain_submissions ("TenantId" uuid,"ChainRunId" uuid,"Status" text,"HeartbeatAt" timestamptz,"FinishedAt" timestamptz,"ClaimedBy" text,"QueueId" uuid,
              "Attempts" int NOT NULL DEFAULT 0,"MaxAttempts" int NOT NULL DEFAULT 1,"PreviousRunIds" uuid[] NOT NULL DEFAULT ARRAY[]::uuid[],
              "NextAttemptAt" timestamptz,"ExpiresAt" timestamptz,"StartedAt" timestamptz,"ClaimedAt" timestamptz,
              "RetryDelaySeconds" int NOT NULL DEFAULT 30,"MessageTtlSeconds" int NOT NULL DEFAULT 3600);
            CREATE TABLE tenants ("Id" uuid,"Slug" text,"Name" text,"TimeZoneId" text);
            CREATE TABLE rpc_job_submissions ("TenantId" uuid,"RunId" uuid,"Status" text,"FinishedAt" timestamptz,
              "ClaimedBy" text,"ClaimedAt" timestamptz,"HeartbeatAt" timestamptz,"LastError" text);
            CREATE TABLE tenant_execution_leases ("TenantId" uuid,"RunKey" text,"ExpiresAt" timestamptz);
            """, connection)) await setup.ExecuteNonQueryAsync();
        try
        {
            var tenant = new FakeCurrentTenant(Guid.NewGuid());
            var other = new FakeCurrentTenant(Guid.NewGuid());
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            var old = DateTimeOffset.UtcNow.AddMinutes(-15);
            var examples = new Dictionary<string, ChainRun>();
            foreach (var kind in new[] { "abandoned", "heartbeat", "lease", "retry", "waiting", "human", "old-failure", "failed-step", "cancelled-job", "other" })
            {
                var owner = kind == "other" ? other : tenant;
                var chain = JobChain.Create(Guid.NewGuid(), kind, null, new[] { Guid.NewGuid(), Guid.NewGuid() }, old);
                var run = ChainRun.Start(chain, new[] { "first", "later" }, old);
                var jobRun = Guid.NewGuid();
                run.MarkStepRunning(0, jobRun, old);
                if (kind == "waiting") run.RetryStep(0, "checkpoint", DateTimeOffset.UtcNow.AddMinutes(1), "retry");
                if (kind == "old-failure") run.Complete(ChainRunStatus.Failed, null, old);
                if (kind == "failed-step") run.MarkStepFinished(0, jobRun, ChainStepStatus.Failed, old);
                if (kind == "human") run.AwaitInput(0, "checkpoint", "{}", null);
                await using var db = new AppDbContext(options, owner);
                await new EfChainRunRepository(db, encryptor).AddAsync(run);
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO jobs VALUES ({chain.StepJobIds[0]}, {owner.TenantId}, 'first', 0);
                    INSERT INTO job_runs VALUES ({jobRun},{owner.TenantId},{chain.StepJobIds[0]},{chain.ProjectId},{(kind == "cancelled-job" ? "Cancelled" : "Failed")},NULL,1,{old},{old});
                    INSERT INTO rpc_chain_submissions ("TenantId","ChainRunId","Status","HeartbeatAt","FinishedAt","ClaimedBy","QueueId") VALUES ({owner.TenantId},{run.Id},'Running',{(kind == "heartbeat" ? DateTimeOffset.UtcNow : old)},NULL,{(kind == "heartbeat" ? "worker" : null)},NULL);
                    """);
                if (kind == "lease") await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO tenant_execution_leases VALUES ({owner.TenantId}, {$"workflow:{run.Id:N}"}, {DateTimeOffset.UtcNow.AddMinutes(1)})");
                if (kind == "retry") await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO job_runs VALUES ({Guid.NewGuid()},{owner.TenantId},{chain.StepJobIds[0]},{chain.ProjectId},'Running',{jobRun},2,{old},NULL)");
                if (kind == "cancelled-job")
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rpc_chain_submissions SET \"QueueId\"={Guid.NewGuid()} WHERE \"ChainRunId\"={run.Id}");
                }
                examples[kind] = run;
            }
            await using var current = new AppDbContext(options, tenant);
            // More than one page of historical records, including an unreadable record first.
            for (var i = 1; i <= 105; i++)
                current.ChainRuns.Add(new ChainRunRow { Id = Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"),
                    ChainId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), ChainName = "historic",
                    Status = "Failed", StartedAt = old, FinishedAt = old,
                    StepsJson = encryptor.Protect(i == 1 ? "broken json" : "[]", IDataEncryptor.Purpose.ChainRun) });
            await current.SaveChangesAsync();
            var reconciler = new DbRunStatusReconciler(current, encryptor);
            var reader = new DbRunStatusReader(current, encryptor);
            var notifier = new FakeRunStatusNotifier();
            var watcher = new RunStatusWatchService(reader, notifier, new FakeClock(DateTimeOffset.UtcNow));
            var state = new RunWatchState();
            Assert.Equal(4, await reconciler.ReconcileAsync(default));
            await watcher.SweepAsync(state);
            Assert.Equal(0, await reconciler.ReconcileAsync(default));
            await watcher.SweepAsync(state);
            var repaired = (await new EfChainRunRepository(current, encryptor).GetByIdAsync(examples["abandoned"].Id))!;
            Assert.Equal(ChainRunStatus.Failed, repaired.Status);
            Assert.Equal(ChainStepStatus.Failed, repaired.Steps[0].Status);
            Assert.Equal(ChainStepStatus.Skipped, repaired.Steps[1].Status);
            Assert.Contains("reconciliation job", repaired.Steps[0].Error);
            Assert.Single(notifier.Synced, n => n.Key == RunStatusWatchService.ChainRunKey(repaired.Id));
            Assert.DoesNotContain(notifier.Synced, n => n.Key == RunStatusWatchService.JobRunKey(repaired.Steps[0].RunId!.Value));
            foreach (var kind in new[] { "heartbeat", "lease", "retry", "waiting", "human" })
                Assert.Equal(examples[kind].Status, (await new EfChainRunRepository(current, encryptor).GetByIdAsync(examples[kind].Id))!.Status);
            var cancelled = (await new EfChainRunRepository(current, encryptor).GetByIdAsync(examples["cancelled-job"].Id))!;
            Assert.Equal(ChainRunStatus.Cancelled, cancelled.Status);
            Assert.All(cancelled.Steps, step => Assert.Equal(ChainStepStatus.Cancelled, step.Status));
            Assert.Equal("Cancelled", await current.Database.SqlQuery<string>($"SELECT \"Status\" AS \"Value\" FROM rpc_chain_submissions WHERE \"ChainRunId\"={cancelled.Id}").SingleAsync());
            await using var otherDb = new AppDbContext(options, other);
            Assert.Equal(ChainRunStatus.Running, (await new EfChainRunRepository(otherDb, encryptor).GetByIdAsync(examples["other"].Id))!.Status);
            await current.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenants VALUES ({tenant.TenantId}, 'first', 'first', 'UTC'),({other.TenantId}, 'other', 'other', 'UTC');
                UPDATE rpc_chain_submissions SET "QueueId"={Guid.NewGuid()}, "Status"='RetryPending', "MaxAttempts"=2 WHERE "ChainRunId"={repaired.Id};
                UPDATE rpc_chain_submissions SET "QueueId"={Guid.NewGuid()} WHERE "ChainRunId"={examples["other"].Id};
                INSERT INTO rpc_job_submissions VALUES ({tenant.TenantId},{repaired.Steps[0].RunId},'Running',NULL,NULL,NULL,NULL,NULL);
                """);
            await using var services = new ServiceCollection()
                .AddScoped(_ => new AppDbContext(options, new CurrentTenant()))
                .AddSingleton<IDataEncryptor>(encryptor).AddScoped<DbRunStatusReconciler>()
                .BuildServiceProvider();
            using var worker = new FailedRunReconciliationBackgroundService(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<FailedRunReconciliationBackgroundService>.Instance);
            await worker.SweepAsync(default);
            await worker.SweepAsync(default);
            Assert.False(new CurrentTenant().IsResolved);
            Assert.Equal("Failed", await current.Database.SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM rpc_job_submissions").SingleAsync());
            Assert.Equal(ChainRunStatus.Failed, (await new EfChainRunRepository(otherDb, encryptor).GetByIdAsync(examples["other"].Id))!.Status);
            await using var queued = new NpgsqlCommand("SELECT \"Status\",\"Attempts\",\"ChainRunId\",\"PreviousRunIds\",\"NextAttemptAt\" > now() FROM rpc_chain_submissions WHERE \"TenantId\"=@tenant AND \"QueueId\" IS NOT NULL AND \"Status\" <> 'Cancelled'",connection);
            queued.Parameters.AddWithValue("tenant",tenant.TenantId);
            await using var saved = await queued.ExecuteReaderAsync();
            Assert.True(await saved.ReadAsync());
            Assert.Equal("Queued",saved.GetString(0));
            Assert.Equal(1,saved.GetInt32(1));
            Assert.NotEqual(repaired.Id,saved.GetGuid(2));
            Assert.Equal(new[] { repaired.Id },saved.GetFieldValue<Guid[]>(3));
            Assert.True(saved.GetBoolean(4));
            Assert.False(await saved.ReadAsync());

        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
