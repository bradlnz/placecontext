using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Scheduling;
using PlaceContext.Infrastructure.Tenancy;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ChainInputResumeTests
{
    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES") is null)
                Skip = "Set PLACECONTEXT_TEST_POSTGRES to an isolated test database.";
        }
    }

    [PostgresFact]
    public async Task Input_checkpoint_survives_reload_and_resume_is_atomic_tenant_scoped_and_idempotent()
    {
        var schema = "input_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
            { SearchPath = schema }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand($"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE chain_runs (
              "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ChainId" uuid NOT NULL, "ProjectId" uuid NOT NULL,
              "ChainName" text NOT NULL, "Status" text NOT NULL, "StepsJson" text NOT NULL, "FinalOutput" text,
              "StartedAt" timestamptz NOT NULL, "FinishedAt" timestamptz, "ResumeAt" timestamptz,
              "ResumeStageIndex" integer, "ContinuationClaimedBy" text, "ContinuationClaimedAt" timestamptz,
              "ContinuationOverrides" text, "InputRequest" text);
            """, connection)) await setup.ExecuteNonQueryAsync();
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
        var encryptor = new FakeDataEncryptor();
        CurrentUser.Set(new UserIdentity(Guid.NewGuid(), "Admin"));
        try
        {
            var chain = JobChain.Create(Guid.NewGuid(), "funding", null, new[] { Guid.NewGuid() }, DateTimeOffset.UtcNow);
            var run = ChainRun.Start(chain, new[] { "funding-position" }, DateTimeOffset.UtcNow);
            var request = new ChainInputRequest(Guid.NewGuid(), new[] { new ChainInputStep(0, "funding-position",
                new[] { new JobParameterDto("funding_inputs.client_cash", Type: "number") }) });
            run.AwaitInput(0, "{\"site\":{\"address\":\"retained\"}}", JsonSerializer.Serialize(request), null);
            await using (var db = new AppDbContext(options, tenant))
            {
                await new EfChainRunRepository(db, encryptor).AddAsync(run);
                await db.SaveChangesAsync();
            }
            await using (var db = new AppDbContext(options, tenant))
            {
                var loaded = await new EfChainRunRepository(db, encryptor).GetByIdAsync(run.Id);
                Assert.Equal(ChainRunStatus.AwaitingInput, loaded!.Status);
                Assert.Null(loaded.ResumeAt);
                Assert.Equal(run.InputRequestJson, loaded.InputRequestJson);
                var queue = new DbJobChainSubmissionQueue(db, tenant, new CurrentUser(), encryptor, new Permissions());
                await Assert.ThrowsAsync<ArgumentException>(() => queue.ResumeAsync(run.Id, request.Id, "{\"0\":{}}"));
            }
            await using (var db = new AppDbContext(options, new FakeCurrentTenant(Guid.NewGuid())))
            {
                var other = new FakeCurrentTenant(Guid.NewGuid());
                var queue = new DbJobChainSubmissionQueue(db, other, new CurrentUser(), encryptor, new Permissions());
                await Assert.ThrowsAsync<ArgumentException>(() => queue.ResumeAsync(run.Id, request.Id, "{}"));
            }
            const string values = "{\"0\":{\"funding_inputs.client_cash\":0}}";
            async Task<string> Submit()
            {
                await using var db = new AppDbContext(options, tenant);
                return await new DbJobChainSubmissionQueue(db, tenant, new CurrentUser(), encryptor, new Permissions())
                    .ResumeAsync(run.Id, request.Id, values);
            }
            Assert.All(await Task.WhenAll(Submit(), Submit()), status => Assert.Equal("Waiting", status));
            await using (var db = new AppDbContext(options, tenant))
            {
                var loaded = await new EfChainRunRepository(db, encryptor).GetByIdAsync(run.Id);
                Assert.Equal(ChainRunStatus.Waiting, loaded!.Status);
                Assert.NotNull(loaded.ResumeAt);
                Assert.Equal(run.FinalOutput, loaded.FinalOutput);
                Assert.Contains("client_cash", loaded.ContinuationOverridesJson);
                var queue = new DbJobChainSubmissionQueue(db, tenant, new CurrentUser(), encryptor, new Permissions());
                await Assert.ThrowsAsync<InvalidOperationException>(() => queue.ResumeAsync(run.Id, request.Id, values.Replace(":0", ":1")));
            }
            await using (var db = new AppDbContext(options, tenant))
            {
                var queue = new DbJobChainSubmissionQueue(db, tenant, new CurrentUser(), encryptor, new Permissions());
                Assert.Equal("Waiting", await queue.ResumeAsync(run.Id, request.Id, values));
                var next = new ChainInputRequest(Guid.NewGuid(), new[] { new ChainInputStep(0, "Agent in the loop",
                    new[] { new JobParameterDto("agent_response") }) }, Kind: LoopChainAction.AgentType);
                await using (var workerDb = new AppDbContext(options, tenant))
                {
                    var row = await workerDb.ChainRuns.SingleAsync();
                    row.Status = "AwaitingInput";
                    row.InputRequest = encryptor.Protect(JsonSerializer.Serialize(next), IDataEncryptor.Purpose.ChainRun);
                    await workerDb.SaveChangesAsync();
                }
                Assert.Equal("Waiting", await queue.ResumeAsync(run.Id, next.Id, "{\"0\":{\"agent_response\":\"Verified\"}}"));
                await using var check = new AppDbContext(options, tenant);
                var saved = await new EfChainRunRepository(check, encryptor).GetByIdAsync(run.Id);
                Assert.Contains("Verified", saved!.ContinuationOverridesJson);
                Assert.Equal(next.Id, JsonSerializer.Deserialize<ChainInputRequest>(saved.InputRequestJson!)!.Id);
            }
            await using var services = new ServiceCollection()
                .AddScoped(_ => new AppDbContext(options, tenant)).BuildServiceProvider();
            using var worker = new ChainContinuationWorker(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ChainContinuationWorker>.Instance);
            var release = typeof(ChainContinuationWorker).GetMethod("ReleaseAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            foreach (var (before, after) in new[] { ("Running", "Waiting"), ("Waiting", "Waiting"),
                ("AwaitingInput", "AwaitingInput"), ("Cancelled", "Cancelled"), ("Succeeded", "Succeeded") })
            {
                await using (var db = new AppDbContext(options, tenant))
                {
                    var row = await db.ChainRuns.SingleAsync();
                    row.Status = before;
                    row.ContinuationClaimedBy = "failed-dispatch";
                    row.ContinuationClaimedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync();
                }
                await (Task)release.Invoke(worker, new object[] { run.Id, CancellationToken.None })!;
                await using var check = new AppDbContext(options, tenant);
                var released = await check.ChainRuns.SingleAsync();
                Assert.Equal(after, released.Status);
                Assert.Equal(after == "Waiting" ? null : "failed-dispatch", released.ContinuationClaimedBy);
                Assert.Equal(0, released.ResumeStageIndex);
                Assert.NotNull(released.InputRequest);
            }
        }
        finally
        {
            CurrentUser.Clear();
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class Permissions : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
