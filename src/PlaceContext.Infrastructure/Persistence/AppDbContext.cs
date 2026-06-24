using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// EF Core (code-first, PostgreSQL) persistence context. Maps flat row POCOs — never the rich Domain
/// aggregates — so the Domain stays free of any ORM concern; repositories translate between the two.
/// Doubles as the <see cref="IUnitOfWork"/> commit boundary for a request scope.
///
/// Multi-tenancy is enforced here: every <see cref="ITenantOwned"/> entity has a global query filter
/// scoping reads to <see cref="ICurrentTenant.TenantId"/>, and <see cref="SaveChangesAsync"/> stamps the
/// current tenant onto new rows. The <c>tenants</c> registry itself is not tenant-scoped.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    private readonly ICurrentTenant _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant) : base(options)
        => _tenant = tenant;

    public DbSet<TenantRow> Tenants => Set<TenantRow>();
    public DbSet<OAuthClientRow> OAuthClients => Set<OAuthClientRow>();
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<ProjectRow> Projects => Set<ProjectRow>();
    public DbSet<ChangeRecordRow> ChangeRecords => Set<ChangeRecordRow>();
    public DbSet<DecisionRow> Decisions => Set<DecisionRow>();
    public DbSet<DebtAssessmentRow> DebtAssessments => Set<DebtAssessmentRow>();
    public DbSet<ProjectContextRow> ProjectContexts => Set<ProjectContextRow>();
    public DbSet<CodeRequirementsRow> CodeRequirements => Set<CodeRequirementsRow>();
    public DbSet<UsageRow> UsageRecords => Set<UsageRow>();
    public DbSet<WorkItemRow> WorkItems => Set<WorkItemRow>();
    public DbSet<ToolCallRow> ToolCalls => Set<ToolCallRow>();

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampTenant();
        return base.SaveChangesAsync(ct);
    }

    public override int SaveChanges()
    {
        StampTenant();
        return base.SaveChanges();
    }

    /// <summary>Stamps the current tenant onto newly-added tenant-owned rows that don't have one yet.</summary>
    private void StampTenant()
    {
        var tenantId = _tenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TenantRow>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<OAuthClientRow>(e =>
        {
            e.ToTable("oauth_clients"); // global registry (not tenant-scoped)
            e.HasKey(x => x.ClientId);
        });

        b.Entity<UserRow>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique(); // email unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ProjectRow>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Path }).IsUnique(); // path unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ChangeRecordRow>(e =>
        {
            e.ToTable("change_records");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Sequence });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<DecisionRow>(e =>
        {
            e.ToTable("decisions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<DebtAssessmentRow>(e =>
        {
            e.ToTable("debt_assessments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.ComputedAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ProjectContextRow>(e =>
        {
            e.ToTable("project_contexts");
            e.HasKey(x => x.ProjectId); // one context document per project (ProjectId is globally unique)
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<CodeRequirementsRow>(e =>
        {
            e.ToTable("code_requirements");
            // Composite key: each tenant has its own per-project docs AND its own global doc (ProjectId = Guid.Empty).
            e.HasKey(x => new { x.TenantId, x.ProjectId });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<UsageRow>(e =>
        {
            e.ToTable("usage_records");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.RecordedAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<WorkItemRow>(e =>
        {
            e.ToTable("work_items");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Status });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ToolCallRow>(e =>
        {
            e.ToTable("tool_calls");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.At);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });
    }
}
