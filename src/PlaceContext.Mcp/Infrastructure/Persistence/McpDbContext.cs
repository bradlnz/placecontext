using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp.Infrastructure.Persistence;

public sealed class McpDbContext : DbContext, IMcpUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public McpDbContext(DbContextOptions<McpDbContext> options, ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<McpConnectionRow> McpConnections => Set<McpConnectionRow>();

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
        modelBuilder.Entity<McpConnectionRow>(entity =>
        {
            entity.ToTable("mcp_connections");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnType("uuid");
            entity.Property(row => row.ProjectId).HasColumnType("uuid");
            entity.Property(row => row.TenantId).HasColumnType("uuid");
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.Transport).HasMaxLength(20);
            entity.Property(row => row.EndpointUrl).HasMaxLength(500);
            entity.Property(row => row.Command).HasMaxLength(200);
            entity.Property(row => row.Args).HasMaxLength(1000);
            entity.Property(row => row.LastStatus).HasMaxLength(200);
            entity.Property(row => row.OAuthClientId).HasMaxLength(200);
            entity.Property(row => row.OAuthScopes).HasMaxLength(500);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<McpConnectionRow>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
        }
    }
}
