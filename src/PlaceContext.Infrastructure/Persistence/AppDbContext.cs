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
    public DbSet<ProjectRow> Projects => Set<ProjectRow>();
    public DbSet<ActivityRecordRow> ActivityRecords => Set<ActivityRecordRow>();
    public DbSet<DecisionRow> Decisions => Set<DecisionRow>();
    public DbSet<RequirementsRow> Requirements => Set<RequirementsRow>();
    public DbSet<UsageRow> UsageRecords => Set<UsageRow>();
    public DbSet<ToolCallRow> ToolCalls => Set<ToolCallRow>();
    public DbSet<JobRow> Jobs => Set<JobRow>();
    public DbSet<JobRunRow> JobRuns => Set<JobRunRow>();
    public DbSet<RunArtifactLinkRow> RunArtifacts => Set<RunArtifactLinkRow>();
    public DbSet<JobSecretRow> JobSecrets => Set<JobSecretRow>();
    public DbSet<JobTriggerRow> JobTriggers => Set<JobTriggerRow>();
    public DbSet<JobChainRow> JobChains => Set<JobChainRow>();
    public DbSet<ChainRunRow> ChainRuns => Set<ChainRunRow>();
    public DbSet<EventDefinitionRow> EventDefinitions => Set<EventDefinitionRow>();
    public DbSet<EventOccurrenceRow> EventOccurrences => Set<EventOccurrenceRow>();
    public DbSet<PendingRunRow> PendingRuns => Set<PendingRunRow>();
    public DbSet<ProjectChartRow> ProjectCharts => Set<ProjectChartRow>();
    public DbSet<DataMappingRow> DataMappings => Set<DataMappingRow>();
    public DbSet<DataEntityRow> DataEntities => Set<DataEntityRow>();
    public DbSet<EntityTagRow> EntityTags => Set<EntityTagRow>();
    public DbSet<RecordLinkRow> RecordLinks => Set<RecordLinkRow>();
    public DbSet<UserApiTokenRow> UserApiTokens => Set<UserApiTokenRow>();
    public DbSet<AgentConfigRow> AgentConfigs => Set<AgentConfigRow>();
    public DbSet<AgentChatSessionRow> AgentChatSessions => Set<AgentChatSessionRow>();
    public DbSet<McpConnectionRow> McpConnections => Set<McpConnectionRow>();
    public DbSet<ChatCommandRow> ChatCommands => Set<ChatCommandRow>();

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
            e.Property(x => x.TwoFactorEnabled).HasDefaultValue(false);
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

        b.Entity<UsageRow>(e =>
        {
            e.ToTable("usage_records");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.RecordedAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ToolCallRow>(e =>
        {
            e.ToTable("tool_calls");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.At);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobRow>(e =>
        {
            e.ToTable("jobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            // New columns added for WorkloadSource discriminated union.
            e.Property(x => x.MapSourceKind).HasDefaultValue("image");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.AllowNetworkEgress).HasDefaultValue(false);
            e.Property(x => x.TimeoutSeconds).HasDefaultValue(300);
            e.Property(x => x.ParametersJson).HasDefaultValue("[]");
            e.Property(x => x.PostJobActionsJson).HasDefaultValue("[]");
            e.Property(x => x.ReturnType).HasDefaultValue("Json");
            e.Property(x => x.RetryCount).HasDefaultValue(0);
            e.Property(x => x.RetryDelaySeconds).HasDefaultValue(0);
        });

        b.Entity<RunArtifactLinkRow>(e =>
        {
            e.ToTable("job_run_artifacts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RunId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<ProjectChartRow>(e =>
        {
            e.ToTable("project_charts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.TableName }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<EntityTagRow>(e =>
        {
            e.ToTable("entity_tags");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityId, x.Key });
            e.HasIndex(x => x.RunId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<RecordLinkRow>(e =>
        {
            e.ToTable("record_links");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.NormalizedValue });
            e.HasIndex(x => new { x.ProjectId, x.TableName, x.RowKey });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<DataEntityRow>(e =>
        {
            e.ToTable("data_entities");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.RelationsJson).HasDefaultValue("[]");
            e.Property(x => x.TagsJson).HasDefaultValue("[]");
        });

        b.Entity<DataMappingRow>(e =>
        {
            e.ToTable("data_mappings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.JobId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.FieldsJson).HasDefaultValue("[]");
            e.Property(x => x.Enabled).HasDefaultValue(true);
            e.Property(x => x.SourceKind).HasDefaultValue("job");
        });

        b.Entity<JobSecretRow>(e =>
        {
            e.ToTable("job_secrets");
            e.HasKey(x => new { x.ProjectId, x.Name });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobRunRow>(e =>
        {
            e.ToTable("job_runs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JobId, x.StartedAt });
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            // SnapshotJson stores the full WorkloadSnapshot at run-start.
            e.Property(x => x.SnapshotJson).HasDefaultValue("{}");
            e.Property(x => x.AttemptNumber).HasDefaultValue(1);
        });

        b.Entity<JobTriggerRow>(e =>
        {
            e.ToTable("job_triggers");
            e.HasKey(x => x.Id);
            e.Property(x => x.JobId).IsRequired(false);
            e.HasIndex(x => x.JobId);
            // Scheduler scans by (Enabled, Kind, NextRunAt) across tenants.
            e.HasIndex(x => new { x.Enabled, x.Kind, x.NextRunAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobChainRow>(e =>
        {
            e.ToTable("job_chains");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<ChainRunRow>(e =>
        {
            e.ToTable("chain_runs");
            e.HasKey(x => x.Id);
            // The pipeline history is read newest-first per chain.
            e.HasIndex(x => new { x.ChainId, x.StartedAt });
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<EventDefinitionRow>(e =>
        {
            e.ToTable("event_definitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); // event name unique within a tenant
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<EventOccurrenceRow>(e =>
        {
            e.ToTable("event_occurrences");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<PendingRunRow>(e =>
        {
            e.ToTable("pending_job_runs"); // global system queue — NOT tenant-filtered
            e.HasKey(x => x.Id);
            // Drained oldest-first among unclaimed rows.
            e.HasIndex(x => new { x.ClaimedAt, x.EnqueuedAt });
        });

        b.Entity<AgentConfigRow>(e =>
        {
            e.ToTable("agent_configs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId).IsUnique(); // one config per project
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.BaseModel).HasDefaultValue("qwen3.5:0.8b");
            e.Property(x => x.SystemPrompt).HasDefaultValue("");
            e.Property(x => x.Preamble).HasDefaultValue("");
            e.Property(x => x.ToolCatalog).HasDefaultValue("");
            e.Property(x => x.LaunchpadToolCatalog).HasDefaultValue("");
            e.Property(x => x.MaxContextChunks).HasDefaultValue(5);
            e.Property(x => x.Temperature).HasDefaultValue(0.7f);
            e.Property(x => x.TopP).HasDefaultValue(0.9f);
            e.Property(x => x.Enabled).HasDefaultValue(true);
        });

        b.Entity<AgentChatSessionRow>(e =>
        {
            e.ToTable("agent_chat_sessions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.UpdatedAt });
            e.Property(x => x.MessagesJson).HasDefaultValue("[]");
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
        });

        b.Entity<McpConnectionRow>(e =>
        {
            e.ToTable("mcp_connections");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnType(DataColumnTypes.Uuid);
            e.Property(r => r.ProjectId).HasColumnType(DataColumnTypes.Uuid);
            e.Property(r => r.TenantId).HasColumnType(DataColumnTypes.Uuid);
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.Transport).HasMaxLength(20);
            e.Property(r => r.EndpointUrl).HasMaxLength(500);
            e.Property(r => r.Command).HasMaxLength(200);
            e.Property(r => r.Args).HasMaxLength(1000);
            e.Property(r => r.LastStatus).HasMaxLength(200);
            e.Property(r => r.OAuthClientId).HasMaxLength(200);
            e.Property(r => r.OAuthScopes).HasMaxLength(500);
            e.HasQueryFilter(r => r.TenantId == _tenant.TenantId);
        });

        b.Entity<ChatCommandRow>(e =>
        {
            e.ToTable("chat_commands");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnType(DataColumnTypes.Uuid);
            e.Property(r => r.ProjectId).HasColumnType(DataColumnTypes.Uuid);
            e.Property(r => r.TenantId).HasColumnType(DataColumnTypes.Uuid);
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.ToolName).HasMaxLength(100);
            e.HasIndex(r => new { r.ProjectId, r.Name }).IsUnique();
            e.HasQueryFilter(r => r.TenantId == _tenant.TenantId);
        });
    }
}
