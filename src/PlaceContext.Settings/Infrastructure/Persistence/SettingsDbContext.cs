using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Settings.Infrastructure.Persistence;

public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
{
    public DbSet<SettingsTenantRow> Tenants => Set<SettingsTenantRow>();
    public DbSet<SettingsUserRow> Users => Set<SettingsUserRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingsTenantRow>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.TimeZoneId).HasColumnName("TimeZoneId");
            entity.Property(row => row.BrandingJson).HasColumnName("BrandingJson");
            entity.Property(row => row.MenuJson).HasColumnName("MenuJson");
            entity.Property(row => row.ArtifactViewJson).HasColumnName("ArtifactViewJson");
        });
        modelBuilder.Entity<SettingsUserRow>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.TenantId).HasColumnName("TenantId");
            entity.Property(row => row.IsDefaultAdmin).HasColumnName("IsDefaultAdmin");
        });
    }
}
