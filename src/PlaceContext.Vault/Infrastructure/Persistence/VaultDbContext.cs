using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Vault.Infrastructure.Persistence;

/// <summary>Vault-owned persistence boundary for encrypted project secrets.</summary>
public sealed class VaultDbContext : DbContext, IVaultUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public VaultDbContext(DbContextOptions<VaultDbContext> options, ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<ProjectSecretRow> ProjectSecrets => Set<ProjectSecretRow>();

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
        modelBuilder.Entity<ProjectSecretRow>(entity =>
        {
            entity.ToTable("job_secrets");
            entity.HasKey(secret => new { secret.ProjectId, secret.Name });
            entity.HasQueryFilter(secret => secret.TenantId == _currentTenant.TenantId);
            entity.Property(secret => secret.Name).HasMaxLength(200);
            entity.Property(secret => secret.Cipher).IsRequired();
            entity.HasIndex(secret => secret.TenantId);
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<ProjectSecretRow>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
        }
    }
}
