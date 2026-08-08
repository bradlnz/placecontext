using Microsoft.EntityFrameworkCore;
using PlaceContext.Agents.Domain.Persistence;
using PlaceContext.Application.Ports;

namespace PlaceContext.Agents.Infrastructure.Persistence;

public sealed class AgentsDbContext : DbContext, IAgentsUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public AgentsDbContext(DbContextOptions<AgentsDbContext> options, ICurrentTenant currentTenant)
        : base(options) => _currentTenant = currentTenant;

    public DbSet<AgentProfileRow> Profiles => Set<AgentProfileRow>();
    public DbSet<StaffMemberRow> Staff => Set<StaffMemberRow>();
    public DbSet<AgentAssignmentRow> Assignments => Set<AgentAssignmentRow>();
    public DbSet<AgentApprovalRow> Approvals => Set<AgentApprovalRow>();

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
        modelBuilder.Entity<AgentProfileRow>(entity =>
        {
            entity.ToTable("agent_profiles");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Name }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.Name).HasMaxLength(120);
            entity.Property(row => row.Role).HasMaxLength(120);
            entity.Property(row => row.Provider).HasMaxLength(80);
            entity.Property(row => row.Model).HasMaxLength(160);
            entity.Property(row => row.ReasoningLevel).HasMaxLength(40);
            entity.Property(row => row.MaxCostPerAssignment).HasPrecision(18, 4);
        });
        modelBuilder.Entity<StaffMemberRow>(entity =>
        {
            entity.ToTable("agent_staff");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Name }).IsUnique();
            entity.HasIndex(row => row.ProfileId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.Name).HasMaxLength(120);
            entity.Property(row => row.Status).HasMaxLength(24);
            entity.Property(row => row.ModelOverride).HasMaxLength(160);
        });
        modelBuilder.Entity<AgentAssignmentRow>(entity =>
        {
            entity.ToTable("agent_assignments");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.ProjectId, row.CreatedAt });
            entity.HasIndex(row => new { row.StaffMemberId, row.Status });
            entity.HasIndex(row => row.ParentAssignmentId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.Status).HasMaxLength(32);
        });
        modelBuilder.Entity<AgentApprovalRow>(entity =>
        {
            entity.ToTable("agent_approvals");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Status, row.RequestedAt });
            entity.HasIndex(row => row.AssignmentId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.ActionKind).HasMaxLength(80);
            entity.Property(row => row.Status).HasMaxLength(24);
        });
    }

    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<IAgentsTenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = _currentTenant.TenantId;
        }
    }
}
