using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ProjectAssignmentTests
{
    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly FakeCurrentTenant _tenant = new(Guid.NewGuid());
    private readonly Caller _admin = new();
    private readonly Caller _member = new();
    private readonly Guid _allowed = Guid.NewGuid(), _denied = Guid.NewGuid(), _job = Guid.NewGuid();

    private async Task SeedAsync()
    {
        await using var db = new AppDbContext(_options, _tenant);
        db.Users.AddRange(new UserRow { Id = _admin.UserId, IsDefaultAdmin = true }, new UserRow { Id = _member.UserId });
        db.Projects.AddRange(new ProjectRow { Id = _allowed, Name = "Allowed", Path = "/allowed" }, new ProjectRow { Id = _denied, Name = "Denied", Path = "/denied" });
        db.Jobs.AddRange(new JobRow { Id = Guid.NewGuid(), ProjectId = _allowed, Name = "Allowed" }, new JobRow { Id = _job, ProjectId = _denied, Name = "Denied" });
        db.RunArtifacts.AddRange(new RunArtifactLinkRow { Id = Guid.NewGuid(), ProjectId = _allowed }, new RunArtifactLinkRow { Id = Guid.NewGuid(), ProjectId = _denied });
        await db.SaveChangesAsync();
    }

    private ProjectAssignments Service(AppDbContext db, Caller user) => new(db, user, _tenant, new EfWorkspaceAdministratorReader(db));

    [Fact]
    public async Task Assignments_filter_projects_jobs_artifacts_and_revocations_apply_to_existing_contexts()
    {
        await SeedAsync();
        await using var adminDb = new AppDbContext(_options, _tenant, _admin);
        await using var memberDb = new AppDbContext(_options, _tenant, _member);
        Assert.Equal(2, await memberDb.Projects.CountAsync());
        await Service(adminDb, _admin).SaveAsync(_member.UserId, [_allowed]);
        Assert.Equal(_allowed, (await memberDb.Projects.SingleAsync()).Id);
        Assert.Equal("Allowed", (await memberDb.Jobs.SingleAsync()).Name);
        Assert.Equal(_allowed, (await memberDb.RunArtifacts.SingleAsync()).ProjectId);
        Assert.Null(await memberDb.Jobs.SingleOrDefaultAsync(j => j.Id == _job));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EfProjectSecretRepository(memberDb).GetCiphersAsync(_denied));
        await Service(adminDb, _admin).SaveAsync(_member.UserId, []);
        Assert.Empty(await memberDb.Projects.ToListAsync());
        Assert.Empty(await memberDb.Jobs.ToListAsync());
        Assert.Equal(2, await adminDb.Projects.CountAsync());
        await Service(adminDb, _admin).SaveAsync(_member.UserId, null);
        Assert.Equal(2, await memberDb.Projects.CountAsync());
    }

    [Fact]
    public async Task Assignment_changes_require_admin_and_reject_foreign_projects_and_admin_lockout()
    {
        await SeedAsync();
        await using var adminDb = new AppDbContext(_options, _tenant, _admin);
        await Assert.ThrowsAsync<ArgumentException>(() => Service(adminDb, _admin).SaveAsync(_member.UserId, [Guid.NewGuid()]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(adminDb, _admin).SaveAsync(_admin.UserId, []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(adminDb, _admin).SaveAsync(Guid.NewGuid(), []));
        await using var memberDb = new AppDbContext(_options, _tenant, _member);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(memberDb, _member).SaveAsync(_member.UserId, null));
    }

    [Fact]
    public async Task Restricted_users_cannot_insert_or_forge_updates_into_other_projects()
    {
        await SeedAsync();
        await using (var adminDb = new AppDbContext(_options, _tenant, _admin)) await Service(adminDb, _admin).SaveAsync(_member.UserId, [_allowed]);
        await using (var db = new AppDbContext(_options, _tenant, _member))
        {
            db.Jobs.Add(new() { Id = Guid.NewGuid(), ProjectId = _denied });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        }
        await using (var db = new AppDbContext(_options, _tenant, _member))
        {
            db.Jobs.Update(new() { Id = _job, TenantId = _tenant.TenantId, ProjectId = _allowed, Name = "Forged" });
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }
        await using var check = new AppDbContext(_options, _tenant, _admin);
        Assert.Equal("Denied", (await check.Jobs.SingleAsync(j => j.Id == _job)).Name);
    }

    [Fact]
    public async Task Ambient_identity_and_new_project_creation_keep_assignment_scope()
    {
        await SeedAsync();
        await using (var db = new AppDbContext(_options, _tenant, _admin)) await Service(db, _admin).SaveAsync(_member.UserId, []);
        CurrentUser.Set(new(_member.UserId, "Member"));
        try
        {
            await using var db = new AppDbContext(_options, _tenant, new CurrentUser());
            Assert.Empty(await db.Projects.ToListAsync());
            var created = new ProjectRow { Id = Guid.NewGuid(), Name = "Created", Path = "/created" };
            db.Projects.Add(created);
            await db.SaveChangesAsync();
            Assert.Equal(created.Id, (await db.Projects.SingleAsync()).Id);
            Assert.Equal(created.Id, Assert.Single((await db.Users.SingleAsync(u => u.Id == _member.UserId)).AssignedProjectIds!));
        }
        finally { CurrentUser.Clear(); }
    }

    [Fact]
    public void PostgreSql_queries_include_project_assignment_checks()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused").Options;
        using var db = new AppDbContext(options, _tenant, _member);
        var sql = db.Jobs.ToQueryString();
        Assert.Contains("AssignedProjectIds", sql);
        Assert.Contains("projects", sql);
        Assert.Contains("users", sql);
    }

    private sealed class Caller : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => "Member";
        public bool IsAuthenticated => true;
    }
}
