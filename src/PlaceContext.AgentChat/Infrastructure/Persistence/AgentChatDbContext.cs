using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.AgentChat.Infrastructure.Persistence;

/// <summary>AgentChat-owned persistence boundary for chat, agent, MCP, and command state.</summary>
public sealed class AgentChatDbContext : DbContext, IAgentChatUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public AgentChatDbContext(
        DbContextOptions<AgentChatDbContext> options,
        ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<AgentConfigRow> AgentConfigs => Set<AgentConfigRow>();
    public DbSet<AgentChatSessionRow> AgentChatSessions => Set<AgentChatSessionRow>();
    public DbSet<McpConnectionRow> McpConnections => Set<McpConnectionRow>();
    public DbSet<ChatCommandRow> ChatCommands => Set<ChatCommandRow>();

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
        modelBuilder.Entity<AgentConfigRow>(entity =>
        {
            entity.ToTable("agent_configs");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ProjectId).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.BaseModel).HasDefaultValue("qwen3.5:0.8b");
            entity.Property(row => row.SystemPrompt).HasDefaultValue(string.Empty);
            entity.Property(row => row.Preamble).HasDefaultValue(string.Empty);
            entity.Property(row => row.ToolCatalog).HasDefaultValue(string.Empty);
            entity.Property(row => row.LaunchpadToolCatalog).HasDefaultValue(string.Empty);
            entity.Property(row => row.MaxContextChunks).HasDefaultValue(5);
            entity.Property(row => row.Temperature).HasDefaultValue(0.7f);
            entity.Property(row => row.TopP).HasDefaultValue(0.9f);
            entity.Property(row => row.Enabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<AgentChatSessionRow>(entity =>
        {
            entity.ToTable("agent_chat_sessions");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.UpdatedAt });
            entity.Property(row => row.MessagesJson).HasDefaultValue("[]");
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<McpConnectionRow>(entity =>
        {
            entity.ToTable("mcp_connections");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnType(DataColumnTypes.Uuid);
            entity.Property(row => row.ProjectId).HasColumnType(DataColumnTypes.Uuid);
            entity.Property(row => row.TenantId).HasColumnType(DataColumnTypes.Uuid);
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

        modelBuilder.Entity<ChatCommandRow>(entity =>
        {
            entity.ToTable("chat_commands");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnType(DataColumnTypes.Uuid);
            entity.Property(row => row.ProjectId).HasColumnType(DataColumnTypes.Uuid);
            entity.Property(row => row.TenantId).HasColumnType(DataColumnTypes.Uuid);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.ToolName).HasMaxLength(100);
            entity.HasIndex(row => new { row.ProjectId, row.Name }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<IAgentChatTenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
        }
    }
}
