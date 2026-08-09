using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;

namespace PlaceContext.Projects.Infrastructure.Persistence;

/// <summary>Projects-owned PostgreSQL persistence boundary.</summary>
public sealed class ProjectsDbContext : DbContext, IUnitOfWork
{
    private readonly ICurrentTenant _tenant;

    public ProjectsDbContext(DbContextOptions<ProjectsDbContext> options, ICurrentTenant tenant)
        : base(options) => _tenant = tenant;

    public DbSet<ProjectRow> Projects => Set<ProjectRow>();
    public DbSet<ActivityRecordRow> ActivityRecords => Set<ActivityRecordRow>();
    public DbSet<DecisionRow> Decisions => Set<DecisionRow>();
    public DbSet<RequirementsRow> Requirements => Set<RequirementsRow>();

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectRow>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Path }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<ActivityRecordRow>(entity =>
        {
            entity.ToTable("activity_log");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.Sequence });
            entity.HasQueryFilter(row => row.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<DecisionRow>(entity =>
        {
            entity.ToTable("decisions");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ProjectId);
            entity.HasQueryFilter(row => row.TenantId == _tenant.TenantId);
        });

        modelBuilder.Entity<RequirementsRow>(entity =>
        {
            entity.ToTable("requirements");
            entity.HasKey(row => new { row.TenantId, row.ProjectId });
            entity.HasQueryFilter(row => row.TenantId == _tenant.TenantId);
        });
    }

    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = _tenant.TenantId;
        }
    }
}
