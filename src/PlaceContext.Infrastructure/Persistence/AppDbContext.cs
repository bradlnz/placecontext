using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// EF Core (code-first, PostgreSQL) persistence context. Maps flat row POCOs — never the rich Domain
/// aggregates — so the Domain stays free of any ORM concern; repositories translate between the two.
/// Doubles as the <see cref="IUnitOfWork"/> commit boundary for a request scope.
///
/// Multi-tenancy is enforced here: every <see cref="ITenantOwned"/> entity has a global query filter
/// scoping reads to <see cref="ICurrentTenant.TenantId"/>, and <see cref="SaveChangesAsync"/> stamps the
/// current tenant onto new rows. The <c>tenants</c> registry itself is not tenant-scoped.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork, IDataProtectionKeyContext
{
    private readonly ICurrentTenant _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant) : base(options)
        => _tenant = tenant;

    /// <summary>Shared ASP.NET Data Protection key ring (so every replica decrypts the same auth cookie).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();
    public DbSet<OAuthClientRow> OAuthClients => Set<OAuthClientRow>();
    public DbSet<OAuthRefreshTokenRow> OAuthRefreshTokens => Set<OAuthRefreshTokenRow>();
    public DbSet<OAuthAuthCodeRow> OAuthAuthCodes => Set<OAuthAuthCodeRow>();
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<InviteRow> Invites => Set<InviteRow>();
    public DbSet<UserPermissionGrantRow> UserPermissionGrants => Set<UserPermissionGrantRow>();
    public DbSet<RoleDefinitionRow> RoleDefinitions => Set<RoleDefinitionRow>();
    public DbSet<ProjectRow> Projects => Set<ProjectRow>();
    public DbSet<ActivityRecordRow> ActivityRecords => Set<ActivityRecordRow>();
    public DbSet<DecisionRow> Decisions => Set<DecisionRow>();
    public DbSet<RequirementsRow> Requirements => Set<RequirementsRow>();
    public DbSet<ToolCallRow> ToolCalls => Set<ToolCallRow>();
    public DbSet<CrmClientRow> CrmClients => Set<CrmClientRow>();
    public DbSet<CrmJobRunRow> CrmJobRuns => Set<CrmJobRunRow>();
    public DbSet<CrmChainRunRow> CrmChainRuns => Set<CrmChainRunRow>();
    public DbSet<CrmCommunicationRow> CrmCommunications => Set<CrmCommunicationRow>();
    public DbSet<CrmAppointmentRow> CrmAppointments => Set<CrmAppointmentRow>();
    public DbSet<CrmCalendarRow> CrmCalendars => Set<CrmCalendarRow>();
    public DbSet<CrmClientArtifactRow> CrmClientArtifacts => Set<CrmClientArtifactRow>();
    public DbSet<CrmClientJobChainAssignmentRow> CrmClientJobChainAssignments => Set<CrmClientJobChainAssignmentRow>();
    public DbSet<CrmAutomationRuleRow> CrmAutomationRules => Set<CrmAutomationRuleRow>();
    public DbSet<CrmAutomationQueueRow> CrmAutomationQueue => Set<CrmAutomationQueueRow>();
    public DbSet<CrmIngestionSettingsRow> CrmIngestionSettings => Set<CrmIngestionSettingsRow>();
    public DbSet<CommunicationProviderRow> CommunicationProviders => Set<CommunicationProviderRow>();
    public DbSet<UserApiTokenRow> UserApiTokens => Set<UserApiTokenRow>();

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

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

    /// <summary>Stamps the current tenant onto newly-added tenant-owned rows that don't have one yet.</summary>
    private void StampTenant()
    {
        var tenantId = _tenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
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
            e.ToTable("oauth_clients"); // global registry (not tenant-scoped)
            e.HasKey(x => x.ClientId);
        });

        b.Entity<OAuthRefreshTokenRow>(e =>
        {
            e.ToTable("oauth_refresh_tokens"); // global (the row carries its tenant)
            e.HasKey(x => x.TokenHash);
            e.HasIndex(x => x.ExpiresAt); // purge scans
        });

        b.Entity<OAuthAuthCodeRow>(e =>
        {
            e.ToTable("oauth_auth_codes"); // global (the row carries its tenant)
            e.HasKey(x => x.CodeHash);
            e.HasIndex(x => x.ExpiresAt); // purge scans
        });

        b.Entity<UserRow>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique(); // email unique within a tenant
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
            e.HasIndex(x => new { x.TenantId, x.UserId, x.Permission }).IsUnique(); // one override per (user, permission)
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<RoleDefinitionRow>(e =>
        {
            e.ToTable("role_definitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); // role name unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.IsSystem).HasDefaultValue(false);
            e.Property(x => x.PermissionsJson).HasDefaultValue("[]");
        });

        b.Entity<ProjectRow>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Path }).IsUnique(); // path unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ActivityRecordRow>(e =>
        {
            e.ToTable("activity_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Sequence });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<DecisionRow>(e =>
        {
            e.ToTable("decisions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<RequirementsRow>(e =>
        {
            e.ToTable("requirements");
            // Composite key: each tenant has its own per-project docs AND its own global doc (ProjectId = Guid.Empty).
            e.HasKey(x => new { x.TenantId, x.ProjectId });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ToolCallRow>(e =>
        {
            e.ToTable("tool_calls");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.At);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<CrmClientRow>(e =>
        {
            e.ToTable("crm_clients");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.LifecycleStage });
            e.HasIndex(x => new { x.ProjectId, x.Email });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.LifecycleStage).HasDefaultValue("Lead");
            e.Property(x => x.CustomerPortalEnabled).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmIngestionSettingsRow>(e =>
        {
            e.ToTable("crm_ingestion_settings");
            e.HasKey(x => x.ProjectId);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.AllowedOrigin);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmJobRunRow>(e =>
        {
            e.ToTable("crm_job_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClientId, x.StartedAt });
            e.HasIndex(x => x.RunId).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.StartedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmChainRunRow>(e =>
        {
            e.ToTable("crm_chain_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClientId, x.StartedAt });
            e.HasIndex(x => x.ChainRunId).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.StartedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmCommunicationRow>(e =>
        {
            e.ToTable("crm_communications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClientId, x.CreatedAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmAppointmentRow>(e =>
        {
            e.ToTable("crm_appointments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.StartsAt });
            e.HasIndex(x => x.ClientId);
            e.HasIndex(x => x.CalendarId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmCalendarRow>(e =>
        {
            e.ToTable("crm_calendars"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmClientArtifactRow>(e =>
        {
            e.ToTable("crm_client_artifacts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClientId, x.CreatedAt });
            e.HasIndex(x => new { x.ClientId, x.SourceArtifactId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmClientJobChainAssignmentRow>(e =>
        {
            e.ToTable("crm_client_job_chain_assignments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.ClientId, x.ChainId }).IsUnique();
            e.HasIndex(x => new { x.ProjectId, x.ClientId });
            e.HasIndex(x => x.ChainId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmAutomationRuleRow>(e =>
        {
            e.ToTable("crm_automation_rules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.EventType, x.LifecycleStage });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.Enabled).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CrmAutomationQueueRow>(e =>
        {
            e.ToTable("crm_automation_queue");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CompletedAt, x.FailedAt, x.ClaimedAt, x.NextAttemptAt });
            e.HasIndex(x => new { x.TenantId, x.ProjectId, x.Id });
            e.HasIndex(x => x.ChainRunId);
        });

        b.Entity<CommunicationProviderRow>(e =>
        {
            e.ToTable("communication_providers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Channel });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.Channel).HasMaxLength(10);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.AuthType).HasMaxLength(10);
            e.Property(x => x.AuthHeaderName).HasMaxLength(100);
            e.Property(x => x.ApiKeySecretName).HasMaxLength(200);
            e.Property(x => x.Enabled).HasDefaultValue(true);
            e.Property(x => x.IsDefault).HasDefaultValue(false);
            e.Property(x => x.UseForTwoFactor).HasDefaultValue(false);
            e.Property(x => x.SettingsJson).HasDefaultValue("{}");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

    }
}
