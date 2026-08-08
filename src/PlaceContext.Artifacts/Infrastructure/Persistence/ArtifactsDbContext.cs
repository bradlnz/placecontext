using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Artifacts.Infrastructure.Persistence;

/// <summary>Artifacts-owned persistence boundary for stored output links and public shares.</summary>
public sealed class ArtifactsDbContext : DbContext, IArtifactsUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public ArtifactsDbContext(
        DbContextOptions<ArtifactsDbContext> options,
        ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<RunArtifactLinkRow> RunArtifacts => Set<RunArtifactLinkRow>();
    public DbSet<ArtifactShareTokenRow> ArtifactShareTokens => Set<ArtifactShareTokenRow>();

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
        modelBuilder.Entity<RunArtifactLinkRow>(entity =>
        {
            entity.ToTable("job_run_artifacts");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.RunId);
            entity.HasIndex(row => row.OcrProcessedAt)
                .HasFilter("\"OcrProcessedAt\" IS NULL")
                .HasDatabaseName("ix_job_run_artifacts_ocr");
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<ArtifactShareTokenRow>(entity =>
        {
            entity.ToTable("artifact_share_tokens");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ArtifactId).IsUnique();
            entity.HasIndex(row => row.TokenHash).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.TokenHash).HasMaxLength(64);
            entity.Property(row => row.TokenPrefix).HasMaxLength(20);
            entity.HasOne<RunArtifactLinkRow>()
                .WithMany()
                .HasForeignKey(row => row.ArtifactId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<IArtifactsTenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
        }
    }
}
