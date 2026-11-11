using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Access;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Secrets;
using PlaceContext.Application.Tenancy;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Build;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Security;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class WorkflowDemoSharesTests
{
    [Fact]
    public async Task Snapshot_is_encrypted_bounded_and_only_contains_explicit_outcome_and_selected_replay()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var link = await fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "Approved demo outcome", 0, 7);
        using var scope = fixture.Provider.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().WorkflowDemoShares.SingleAsync();
        Assert.DoesNotContain(link.Token, row.TokenHash);
        Assert.DoesNotContain("Approved demo", row.Content);
        fixture.Identity.Authenticated = false;
        fixture.Identity.TenantId = Guid.Empty;
        var demo = await fixture.Shares.ResolveAsync(link.Token);
        Assert.Equal("Approved demo outcome", demo!.Outcome);
        Assert.Single(demo.Frames);
        Assert.DoesNotContain("private run input", System.Text.Json.JsonSerializer.Serialize(demo));
        Assert.NotNull(demo.Frames[0].Image);
        Assert.Equal(fixture.Clock.UtcNow.AddDays(7), demo.ExpiresAt);
    }

    [Fact]
    public async Task Rotation_revocation_expiry_and_deleted_run_block_public_links()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var first = await fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "one", null, 7);
        var second = await fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "two", null, 1);
        Assert.Null(await fixture.Shares.ResolveAsync(first.Token));
        Assert.NotNull(await fixture.Shares.ResolveAsync(second.Token));
        await fixture.Shares.RevokeAsync(fixture.Project, fixture.Run.Id);
        Assert.Null(await fixture.Shares.ResolveAsync(second.Token));
        var third = await fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "three", null, 1);
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(1);
        Assert.Null(await fixture.Shares.ResolveAsync(third.Token));
        Assert.Null(await fixture.Shares.GetExpiryAsync(fixture.Project, fixture.Run.Id));
        var fourth = await fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "four", null, 1);
        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChainRuns.Remove(await db.ChainRuns.SingleAsync());
        await db.SaveChangesAsync();
        Assert.Null(await fixture.Shares.ResolveAsync(fourth.Token));
        Assert.Null(await fixture.Shares.ResolveAsync("invalid"));
    }

    [Fact]
    public async Task Management_rechecks_permissions_tenant_project_and_owner()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var link = await fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "safe", null, 7);
        fixture.Identity.UserId = Guid.NewGuid();
        Assert.Null(await fixture.Shares.GetExpiryAsync(fixture.Project, fixture.Run.Id));
        await fixture.Shares.RevokeAsync(fixture.Project, fixture.Run.Id);
        Assert.NotNull(await fixture.Shares.ResolveAsync(link.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shares.CreateAsync(Guid.NewGuid(), fixture.Run.Id, "x", null, 7));
        foreach (var permission in new[] { Permission.ProjectsView, Permission.ChainsManage, Permission.JobsView, Permission.ArtifactsShare })
        {
            fixture.Identity.Denied = permission;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "x", null, 7));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Shares.RevokeAsync(fixture.Project, fixture.Run.Id));
        }
        fixture.Identity.Denied = null;
        fixture.Identity.TenantId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "x", null, 7));
    }

    [Fact]
    public async Task Rejects_missing_evidence_invalid_limits_and_unfinished_runs()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "", null, 7));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "x", 1, 7));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "x", null, 31));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, new string('x', 250001), null, 7));
        fixture.Replay.Captures.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "x", 0, 7));
        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ChainRuns.SingleAsync()).FinishedAt = null;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Shares.CreateAsync(fixture.Project, fixture.Run.Id, "x", null, 7));
    }

    private sealed class Identity : ICurrentTenant, ICurrentUser, IPermissionService
    {
        public Guid TenantId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? Denied;
        public bool Authenticated = true;
        public string Slug => "test";
        public string TimeZoneId => "UTC";
        public bool IsResolved => TenantId != Guid.Empty;
        public string Role => "Owner";
        public bool IsAuthenticated => Authenticated;
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(permission != Denied);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(Permission.All.ToHashSet());
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => GetEffectivePermissionsAsync(ct);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Identity Identity = new();
        public FakeClock Clock = new(DateTimeOffset.UtcNow);
        public FakeVirtualMachineReplayStore Replay = new();
        public Guid Project = Guid.NewGuid();
        public ChainRun Run = null!;
        public ServiceProvider Provider;
        public WorkflowDemoShares Shares;
        private readonly IDataEncryptor _encryption = new DataProtectionEncryptor(new EphemeralDataProtectionProvider());
        public Fixture()
        {
            var services = new ServiceCollection();
            var database = Guid.NewGuid().ToString();
            services.AddSingleton<ICurrentTenant>(Identity);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(database));
            Provider = services.BuildServiceProvider();
            Shares = new(Provider.GetRequiredService<IServiceScopeFactory>(), Identity, Identity, Identity, _encryption, Replay, Clock);
        }
        public async Task SeedAsync()
        {
            var chain = JobChain.Create(Project, "Browser demo", null,
                [ChainStage.ForAction(new VirtualMachineChainAction("browser-linux", "Open the page", "browser_result"))], Clock.UtcNow);
            Run = ChainRun.Start(chain, ["Browser"], Clock.UtcNow);
            Run.MarkStepRunning(0, null, Clock.UtcNow);
            Run.MarkStepFinished(0, null, ChainStepStatus.Succeeded, Clock.UtcNow);
            Run.Complete(ChainRunStatus.Succeeded, "private run input", Clock.UtcNow);
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await new EfChainRunRepository(db, _encryption).AddAsync(Run);
            await db.SaveChangesAsync();
            Replay.Captures.Add((Run.Id, 0, new(new(0, 10, Clock.UtcNow, "Opened page", 0, true), [137, 80, 78, 71, 13, 10, 26, 10])));
        }
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
