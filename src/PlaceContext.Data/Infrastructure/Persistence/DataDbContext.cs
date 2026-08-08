using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Data.Infrastructure.Persistence;

/// <summary>Data-owned persistence boundary for mappings, entities, links, charts, and saved SQL.</summary>
public sealed class DataDbContext : DbContext, IDataUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public DataDbContext(DbContextOptions<DataDbContext> options, ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<ProjectChartRow> ProjectCharts => Set<ProjectChartRow>();
    public DbSet<DataMappingRow> DataMappings => Set<DataMappingRow>();
    public DbSet<DataEntityRow> DataEntities => Set<DataEntityRow>();
    public DbSet<EntityTagRow> EntityTags => Set<EntityTagRow>();
    public DbSet<RecordLinkRow> RecordLinks => Set<RecordLinkRow>();
    public DbSet<SavedQueryRow> SavedQueries => Set<SavedQueryRow>();

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
        modelBuilder.Entity<ProjectChartRow>(entity =>
        {
            entity.ToTable("project_charts");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.TableName }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<EntityTagRow>(entity =>
        {
            entity.ToTable("entity_tags");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.EntityId, row.Key });
            entity.HasIndex(row => row.RunId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<RecordLinkRow>(entity =>
        {
            entity.ToTable("record_links");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.NormalizedValue });
            entity.HasIndex(row => new { row.ProjectId, row.TableName, row.RowKey });
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<DataEntityRow>(entity =>
        {
            entity.ToTable("data_entities");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ProjectId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.RelationsJson).HasDefaultValue("[]");
            entity.Property(row => row.TagsJson).HasDefaultValue("[]");
        });

        modelBuilder.Entity<DataMappingRow>(entity =>
        {
            entity.ToTable("data_mappings");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ProjectId);
            entity.HasIndex(row => row.JobId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.FieldsJson).HasDefaultValue("[]");
            entity.Property(row => row.Enabled).HasDefaultValue(true);
            entity.Property(row => row.SourceKind).HasDefaultValue("job");
        });

        modelBuilder.Entity<SavedQueryRow>(entity =>
        {
            entity.ToTable("saved_queries");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.Name }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<IDataTenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
        }
    }
}
