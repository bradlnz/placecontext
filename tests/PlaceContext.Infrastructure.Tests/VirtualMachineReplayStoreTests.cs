using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class VirtualMachineReplayStoreTests
{
    [Fact]
    public async Task Invalid_pointer_metadata_is_rejected_before_storage()
    {
        var store = new DbVirtualMachineReplayStore(new DbContextOptionsBuilder<AppDbContext>().Options,
            new FakeCurrentTenant(Guid.NewGuid()), new DataProtectionEncryptor(new EphemeralDataProtectionProvider()), new Permissions(true));
        var frame = new VirtualMachineReplayFrame(0, 0, DateTimeOffset.UtcNow, "Pointer", 0, false);
        foreach (var invalid in new[] { frame with { PointerX = .5 }, frame with { Click = true },
            frame with { PointerX = double.NaN, PointerY = .5 }, frame with { PointerX = 1.1, PointerY = .5 } })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(Guid.NewGuid(), 0, new(invalid, null), default));
        }
    }

    private sealed class Permissions(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid id, string role, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }

    [ChainInputResumeTests.PostgresFact]
    public async Task Frames_are_encrypted_durable_bounded_and_scoped_to_the_workflow_workspace_and_project()
    {
        var schema = "replay_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
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
            """, connection))
        {
            await setup.ExecuteNonQueryAsync();
        }
        try
        {
            var tenant = new FakeCurrentTenant(Guid.NewGuid());
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;
            var encryptor = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
            await using var db = new AppDbContext(options, tenant);
            PlaceContext.Infrastructure.DependencyInjection.EnsureQemuReplaySchema(db);
            var chain = JobChain.Create(Guid.NewGuid(), "Desktop", null, [ChainStage.ForAction(new VirtualMachineChainAction("desktop", "Open browser"))], DateTimeOffset.UtcNow);
            var run = ChainRun.Start(chain, ["QEMU environment"], DateTimeOffset.UtcNow);
            var repository = new EfChainRunRepository(db, encryptor);
            await repository.AddAsync(run);
            await db.SaveChangesAsync();
            var store = new DbVirtualMachineReplayStore(options, tenant, encryptor, new Permissions(true));
            byte[] png = [137, 80, 78, 71, 13, 10, 26, 10];
            var frame = new VirtualMachineReplayFrame(0, 100, DateTimeOffset.UtcNow, "Private desktop action", 0, true);
            await store.AppendAsync(run.Id, 0, new(frame, png), default);
            await store.AppendAsync(run.Id, 0, new(frame, png), default);
            Assert.Equal(frame, Assert.Single(await store.ListAsync(chain.ProjectId, run.Id, 0, default)));
            Assert.Equal(png, await store.ReadImageAsync(chain.ProjectId, run.Id, 0, 0, default));
            await using (var raw = new NpgsqlCommand("SELECT \"Metadata\", \"Image\" FROM qemu_replay_frames", connection))
            {
                await using (var reader = await raw.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync());
                    Assert.DoesNotContain(frame.Description, reader.GetString(0));
                    Assert.False(png.SequenceEqual(reader.GetFieldValue<byte[]>(1)));
                }
            }
            Assert.Empty(await store.ListAsync(Guid.NewGuid(), run.Id, 0, default));
            Assert.Null(await store.ReadImageAsync(Guid.NewGuid(), run.Id, 0, 0, default));
            Assert.Empty(await store.ListAsync(chain.ProjectId, run.Id, 1, default));
            var other = new DbVirtualMachineReplayStore(options, new FakeCurrentTenant(Guid.NewGuid()), encryptor, new Permissions(true));
            Assert.Empty(await other.ListAsync(chain.ProjectId, run.Id, 0, default));
            Assert.Null(await other.ReadImageAsync(chain.ProjectId, run.Id, 0, 0, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => other.AppendAsync(run.Id, 0, new(frame, png), default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DbVirtualMachineReplayStore(options, tenant, encryptor, new Permissions(false)).ListAsync(chain.ProjectId, run.Id, 0, default));
            await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(run.Id, 0, new(frame with { Sequence = 101 }, png), default));
            await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(run.Id, 0, new(frame, [1, 2, 3]), default));
            var large = new byte[IVirtualMachineReplayStore.MaxImageBytes];
            png.CopyTo(large, 0);
            for (var i = 1; i <= 12; i++)
            {
                await store.AppendAsync(run.Id, 0, new(frame with
                {
                    Sequence = i
                }, large), default);
            }
            var reloaded = new DbVirtualMachineReplayStore(options, tenant, encryptor, new Permissions(true));
            var frames = await reloaded.ListAsync(chain.ProjectId, run.Id, 0, default);
            Assert.False(frames[^1].HasImage);
            Assert.Contains("storage limit", frames[^1].Error);
            Assert.Null(await reloaded.ReadImageAsync(chain.ProjectId, run.Id, 0, frames[^1].Sequence, default));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM chain_runs WHERE \"Id\" = {run.Id}");
            await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM qemu_replay_frames", connection);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
