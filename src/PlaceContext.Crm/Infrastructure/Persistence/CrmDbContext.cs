using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Domain.Persistence;

namespace PlaceContext.Crm.Infrastructure.Persistence;

/// <summary>CRM-owned persistence boundary for clients, automation, calendars, and communications.</summary>
public sealed class CrmDbContext : DbContext, ICrmUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public CrmDbContext(DbContextOptions<CrmDbContext> options, ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<CrmClientRow> CrmClients => Set<CrmClientRow>();
    public DbSet<CrmJobRunRow> CrmJobRuns => Set<CrmJobRunRow>();
    public DbSet<CrmChainRunRow> CrmChainRuns => Set<CrmChainRunRow>();
    public DbSet<CrmCommunicationRow> CrmCommunications => Set<CrmCommunicationRow>();
    public DbSet<CrmAppointmentRow> CrmAppointments => Set<CrmAppointmentRow>();
    public DbSet<CrmCalendarRow> CrmCalendars => Set<CrmCalendarRow>();
    public DbSet<CrmClientArtifactRow> CrmClientArtifacts => Set<CrmClientArtifactRow>();
    public DbSet<CrmClientJobChainAssignmentRow> CrmClientJobChainAssignments =>
        Set<CrmClientJobChainAssignmentRow>();
    public DbSet<CrmAutomationRuleRow> CrmAutomationRules => Set<CrmAutomationRuleRow>();
    public DbSet<CrmAutomationQueueRow> CrmAutomationQueue => Set<CrmAutomationQueueRow>();
    public DbSet<CrmIngestionSettingsRow> CrmIngestionSettings => Set<CrmIngestionSettingsRow>();

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
        modelBuilder.Entity<CrmClientRow>(entity =>
        {
            entity.ToTable("crm_clients");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.LifecycleStage });
            entity.HasIndex(row => new { row.ProjectId, row.Email });
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.LifecycleStage).HasDefaultValue("Lead");
            entity.Property(row => row.CustomerPortalEnabled).HasDefaultValue(false);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmIngestionSettingsRow>(entity =>
        {
            entity.ToTable("crm_ingestion_settings");
            entity.HasKey(row => row.ProjectId);
            entity.HasIndex(row => row.TokenHash).IsUnique();
            entity.HasIndex(row => row.AllowedOrigin);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmJobRunRow>(entity =>
        {
            entity.ToTable("crm_job_runs");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ClientId, row.StartedAt });
            entity.HasIndex(row => row.RunId).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.StartedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmChainRunRow>(entity =>
        {
            entity.ToTable("crm_chain_runs");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ClientId, row.StartedAt });
            entity.HasIndex(row => row.ChainRunId).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.StartedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmCommunicationRow>(entity =>
        {
            entity.ToTable("crm_communications");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ClientId, row.CreatedAt });
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmAppointmentRow>(entity =>
        {
            entity.ToTable("crm_appointments");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.StartsAt });
            entity.HasIndex(row => row.ClientId);
            entity.HasIndex(row => row.CalendarId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmCalendarRow>(entity =>
        {
            entity.ToTable("crm_calendars");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.Name }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmClientArtifactRow>(entity =>
        {
            entity.ToTable("crm_client_artifacts");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ClientId, row.CreatedAt });
            entity.HasIndex(row => new { row.ClientId, row.SourceArtifactId }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmClientJobChainAssignmentRow>(entity =>
        {
            entity.ToTable("crm_client_job_chain_assignments");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.ClientId, row.ChainId }).IsUnique();
            entity.HasIndex(row => new { row.ProjectId, row.ClientId });
            entity.HasIndex(row => row.ChainId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmAutomationRuleRow>(entity =>
        {
            entity.ToTable("crm_automation_rules");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.EventType, row.LifecycleStage });
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.Enabled).HasDefaultValue(true);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<CrmAutomationQueueRow>(entity =>
        {
            entity.ToTable("crm_automation_queue");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.CompletedAt, row.FailedAt, row.ClaimedAt, row.NextAttemptAt });
            entity.HasIndex(row => new { row.TenantId, row.ProjectId, row.Id });
            entity.HasIndex(row => row.ChainRunId);
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<ICrmTenantOwned>())
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
    }
}
