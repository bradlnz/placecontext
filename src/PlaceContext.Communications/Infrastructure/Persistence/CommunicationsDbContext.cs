using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;

namespace PlaceContext.Communications.Infrastructure.Persistence;

public sealed class CommunicationsDbContext(
    DbContextOptions<CommunicationsDbContext> options,
    ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<CommunicationProviderRow> Providers => Set<CommunicationProviderRow>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<CommunicationProviderRow>())
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenant.TenantId;
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommunicationProviderRow>(entity =>
        {
            entity.ToTable("communication_providers");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Channel });
            entity.HasQueryFilter(row => row.TenantId == tenant.TenantId);
            entity.Property(row => row.Channel).HasMaxLength(10);
            entity.Property(row => row.Kind).HasMaxLength(20);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.AuthType).HasMaxLength(10);
            entity.Property(row => row.AuthHeaderName).HasMaxLength(100);
            entity.Property(row => row.ApiKeySecretName).HasMaxLength(200);
            entity.Property(row => row.Enabled).HasDefaultValue(true);
            entity.Property(row => row.IsDefault).HasDefaultValue(false);
            entity.Property(row => row.UseForTwoFactor).HasDefaultValue(false);
            entity.Property(row => row.SettingsJson).HasDefaultValue("{}");
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });
    }
}
