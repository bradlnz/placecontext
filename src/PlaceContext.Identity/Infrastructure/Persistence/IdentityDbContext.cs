using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Identity.Domain.Persistence;

namespace PlaceContext.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : DbContext, IIdentityUnitOfWork, IDataProtectionKeyContext
{
    private readonly ICurrentTenant _tenant;

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, ICurrentTenant tenant)
        : base(options) => _tenant = tenant;

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<TenantRow> Tenants => Set<TenantRow>();
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<InviteRow> Invites => Set<InviteRow>();
    public DbSet<RoleDefinitionRow> RoleDefinitions => Set<RoleDefinitionRow>();
    public DbSet<UserPermissionGrantRow> UserPermissionGrants => Set<UserPermissionGrantRow>();
    public DbSet<UserApiTokenRow> UserApiTokens => Set<UserApiTokenRow>();
    public DbSet<OAuthClientRow> OAuthClients => Set<OAuthClientRow>();
    public DbSet<OAuthRefreshTokenRow> OAuthRefreshTokens => Set<OAuthRefreshTokenRow>();
    public DbSet<OAuthAuthCodeRow> OAuthAuthCodes => Set<OAuthAuthCodeRow>();

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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
        b.Entity<TenantRow>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.CustomerPortalDomain).IsUnique();
            e.Property(x => x.CustomerPortalEnabled).HasDefaultValue(false);
        });
        b.Entity<OAuthClientRow>(e =>
        {
            e.ToTable("oauth_clients");
            e.HasKey(x => x.ClientId);
        });
        b.Entity<OAuthRefreshTokenRow>(e =>
        {
            e.ToTable("oauth_refresh_tokens");
            e.HasKey(x => x.TokenHash);
            e.HasIndex(x => x.ExpiresAt);
        });
        b.Entity<OAuthAuthCodeRow>(e =>
        {
            e.ToTable("oauth_auth_codes");
            e.HasKey(x => x.CodeHash);
            e.HasIndex(x => x.ExpiresAt);
        });
        b.Entity<UserRow>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.PasswordSet).HasDefaultValue(false);
            e.Property(x => x.IsDefaultAdmin).HasDefaultValue(false);
            e.Property(x => x.TwoFactorEnabled).HasDefaultValue(false);
            e.Property(x => x.TwoFactorChannel).HasDefaultValue("email");
            e.Property(x => x.TwoFactorCodeFailedAttempts).HasDefaultValue(0);
        });
        b.Entity<InviteRow>(e =>
        {
            e.ToTable("invites");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });
        b.Entity<UserApiTokenRow>(e =>
        {
            e.ToTable("user_api_tokens");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });
        b.Entity<UserPermissionGrantRow>(e =>
        {
            e.ToTable("user_permission_grants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.Permission }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });
        b.Entity<RoleDefinitionRow>(e =>
        {
            e.ToTable("role_definitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.IsSystem).HasDefaultValue(false);
            e.Property(x => x.PermissionsJson).HasDefaultValue("[]");
        });
    }

    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<IIdentityTenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = _tenant.TenantId;
        }
    }
}
