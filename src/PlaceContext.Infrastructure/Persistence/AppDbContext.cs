using PlaceContext.Application.Ports;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
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
public sealed class AppDbContext : DbContext, IUnitOfWork, IDataProtectionKeyContext
{
    private readonly ICurrentTenant _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant) : base(options)
        => _tenant = tenant;

    /// <summary>Shared ASP.NET Data Protection key ring (so every replica decrypts the same auth cookie).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();
    public DbSet<OAuthClientRow> OAuthClients => Set<OAuthClientRow>();
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<InviteRow> Invites => Set<InviteRow>();
    public DbSet<ProjectRow> Projects => Set<ProjectRow>();
    public DbSet<ActivityRecordRow> ActivityRecords => Set<ActivityRecordRow>();
    public DbSet<DecisionRow> Decisions => Set<DecisionRow>();
    public DbSet<RiskAssessmentRow> RiskAssessments => Set<RiskAssessmentRow>();
    public DbSet<ProjectContextRow> ProjectContexts => Set<ProjectContextRow>();
    public DbSet<RequirementsRow> Requirements => Set<RequirementsRow>();
    public DbSet<UsageRow> UsageRecords => Set<UsageRow>();
    public DbSet<WorkItemRow> WorkItems => Set<WorkItemRow>();
    public DbSet<ReportTemplateRow> ReportTemplates => Set<ReportTemplateRow>();
    public DbSet<ToolCallRow> ToolCalls => Set<ToolCallRow>();
    public DbSet<JobRow> Jobs => Set<JobRow>();
    public DbSet<JobRunRow> JobRuns => Set<JobRunRow>();
    public DbSet<JobSecretRow> JobSecrets => Set<JobSecretRow>();
    public DbSet<JobTriggerRow> JobTriggers => Set<JobTriggerRow>();
    public DbSet<EventDefinitionRow> EventDefinitions => Set<EventDefinitionRow>();
    public DbSet<EventOccurrenceRow> EventOccurrences => Set<EventOccurrenceRow>();
    public DbSet<PendingRunRow> PendingRuns => Set<PendingRunRow>();

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

        b.Entity<InviteRow>(e =>
        {
            e.ToTable("invites");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ProjectRow>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Path }).IsUnique(); // path unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ActivityRecordRow>(e =>
        {
            e.ToTable("activity_log");
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

        b.Entity<RiskAssessmentRow>(e =>
        {
            e.ToTable("risk_assessments");
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

        b.Entity<RequirementsRow>(e =>
        {
            e.ToTable("requirements");
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

        b.Entity<ReportTemplateRow>(e =>
        {
            e.ToTable("report_templates");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); // name unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ToolCallRow>(e =>
        {
            e.ToTable("tool_calls");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.At);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobRow>(e =>
        {
            e.ToTable("jobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            // New columns added for WorkloadSource discriminated union.
            e.Property(x => x.MapSourceKind).HasDefaultValue("image");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.AllowNetworkEgress).HasDefaultValue(false);
            e.Property(x => x.ParametersJson).HasDefaultValue("[]");
        });

        b.Entity<JobSecretRow>(e =>
        {
            e.ToTable("job_secrets");
            e.HasKey(x => new { x.ProjectId, x.Name });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobRunRow>(e =>
        {
            e.ToTable("job_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JobId, x.StartedAt });
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            // SnapshotJson stores the full WorkloadSnapshot at run-start.
            e.Property(x => x.SnapshotJson).HasDefaultValue("{}");
        });

        b.Entity<JobTriggerRow>(e =>
        {
            e.ToTable("job_triggers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.JobId);
            // Scheduler scans by (Enabled, Kind, NextRunAt) across tenants.
            e.HasIndex(x => new { x.Enabled, x.Kind, x.NextRunAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<EventDefinitionRow>(e =>
        {
            e.ToTable("event_definitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); // event name unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<EventOccurrenceRow>(e =>
        {
            e.ToTable("event_occurrences");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<PendingRunRow>(e =>
        {
            e.ToTable("pending_job_runs"); // global system queue — NOT tenant-filtered
            e.HasKey(x => x.Id);
            // Drained oldest-first among unclaimed rows.
            e.HasIndex(x => new { x.ClaimedAt, x.EnqueuedAt });
        });
    }
}
