using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Search.Infrastructure.Persistence;

/// <summary>Search-owned persistence boundary for saved OpenSearch dashboards.</summary>
public sealed class SearchDbContext : DbContext, ISearchUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public SearchDbContext(
        DbContextOptions<SearchDbContext> options,
        ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<OpenSearchDashboardRow> OpenSearchDashboards => Set<OpenSearchDashboardRow>();

    public override int SaveChanges()
    {
        StampTenant();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OpenSearchDashboardRow>(entity =>
        {
            entity.ToTable("opensearch_dashboards");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.Name }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.IndexPattern).HasDefaultValue("*");
            entity.Property(row => row.BucketType).HasDefaultValue("terms");
            entity.Property(row => row.ChartType).HasDefaultValue("bar");
            entity.Property(row => row.MetricType).HasDefaultValue("count");
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<ISearchTenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
        }
    }
}
