using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;

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
    internal DbContextOptions<AppDbContext> ContextOptions
    {
        get;
    }
    public DbSet<PendingWorkspaceSignupRow> PendingWorkspaceSignups => Set<PendingWorkspaceSignupRow>();
    public DbSet<TenantRunPlanRow> TenantRunPlans => Set<TenantRunPlanRow>();
    public DbSet<BillableRunRow> BillableRuns => Set<BillableRunRow>();
    public DbSet<PaymentBillingEventRow> StripeBillingEvents => Set<PaymentBillingEventRow>();
    public DbSet<ManagedOAuthConnectionRow> ManagedOAuthConnections => Set<ManagedOAuthConnectionRow>();
    public DbSet<ManagedOAuthStateRow> ManagedOAuthStates => Set<ManagedOAuthStateRow>();
    public DbSet<AgentSubscriptionRow> AgentSubscriptions => Set<AgentSubscriptionRow>();
    public DbSet<AgentRunRow> AgentRuns => Set<AgentRunRow>();

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant tenant) : base(options)
        => (_tenant, ContextOptions) = (tenant, options);

    /// <summary>Shared ASP.NET Data Protection key ring (so every replica decrypts the same auth cookie).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<ModelConnectionRow> ModelConnections => Set<ModelConnectionRow>();
    public DbSet<BuildConversationRow> BuildConversations => Set<BuildConversationRow>();
    public DbSet<BuildMemoryRow> BuildMemories => Set<BuildMemoryRow>();
    public DbSet<WorkflowDemoShareRow> WorkflowDemoShares => Set<WorkflowDemoShareRow>();

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
    public DbSet<UsageRow> UsageRecords => Set<UsageRow>();
    public DbSet<DataSourceRow> DataSources => Set<DataSourceRow>();
    public DbSet<SourceDocumentRow> SourceDocuments => Set<SourceDocumentRow>();
    public DbSet<ToolCallRow> ToolCalls => Set<ToolCallRow>();
    public DbSet<JobRow> Jobs => Set<JobRow>();
    public DbSet<JobRunRow> JobRuns => Set<JobRunRow>();
    public DbSet<JobTestCaseRow> JobTestCases => Set<JobTestCaseRow>();
    public DbSet<SearchDashboardRow> OpenSearchDashboards => Set<SearchDashboardRow>();
    public DbSet<SavedQueryRow> SavedQueries => Set<SavedQueryRow>();
    public DbSet<CommunicationProviderRow> CommunicationProviders => Set<CommunicationProviderRow>();
    public DbSet<RunArtifactLinkRow> RunArtifacts => Set<RunArtifactLinkRow>();
    public DbSet<ArtifactShareTokenRow> ArtifactShareTokens => Set<ArtifactShareTokenRow>();
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
    public DbSet<McpConnectionRow> McpConnections => Set<McpConnectionRow>();

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken ct) => SaveChangesAsync(ct);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>Stamps new rows and rejects missing or changed tenant ownership before writes.</summary>
    private void StampTenant()
    {
        var tenantId = _tenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }
            if (!_tenant.IsResolved || tenantId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("A resolved tenant is required to write tenant data.");
            }
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = tenantId;
            }
            if (entry.Entity.TenantId != tenantId || (entry.State != EntityState.Added
                            && entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue is Guid original && original != tenantId))
            {
                throw new UnauthorizedAccessException("Tenant ownership cannot be changed or crossed.");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PendingWorkspaceSignupRow>(e =>
        {
            e.ToTable("pending_workspace_signups");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_pending_signup_token");
            e.HasIndex(x => x.ExchangeHash).IsUnique().HasDatabaseName("ux_pending_signup_exchange");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_pending_signup_expiry");
            e.HasIndex(x => x.Slug).HasDatabaseName("ix_pending_signup_slug");
        });

        b.Entity<TenantRunPlanRow>(e =>
        {
            e.ToTable("tenant_run_plans");
            e.HasKey(x => x.TenantId);
            e.HasIndex(x => x.StripeCustomerId).IsUnique().HasDatabaseName("ix_tenant_run_plans_customer");
            e.HasIndex(x => x.StripeSubscriptionId).IsUnique().HasDatabaseName("ix_tenant_run_plans_subscription");
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<BillableRunRow>(e =>
        {
            e.ToTable("billable_runs");
            e.HasKey(x => new { x.TenantId, x.RunKey });
            e.HasIndex(x => new { x.TenantId, x.PeriodStart }).HasDatabaseName("ix_billable_runs_period");
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<PaymentBillingEventRow>(e =>
        {
            e.ToTable("stripe_billing_events");
            e.HasKey(x => x.EventId);
        });
        b.Entity<ManagedOAuthConnectionRow>(e =>
        {
            e.ToTable("managed_oauth_connections");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ProjectId, x.Provider }).IsUnique().HasDatabaseName("ix_managed_oauth_connection_project");
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<ManagedOAuthStateRow>(e =>
        {
            e.ToTable("managed_oauth_states");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StateHash).IsUnique().HasDatabaseName("ix_managed_oauth_states_StateHash");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_managed_oauth_states_expiry");
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<AgentSubscriptionRow>(e =>
        {
            e.ToTable("agent_subscriptions");
            e.HasKey(x => new { x.TenantId, x.AgentId });
            e.Property(x => x.AgentId).HasMaxLength(80);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<AgentRunRow>(e =>
        {
            e.ToTable("agent_runs");
            e.HasKey(x => new { x.TenantId, x.Id });
            e.Property(x => x.AgentId).HasMaxLength(80);
            e.Property(x => x.Status).HasConversion<string>().IsConcurrencyToken();
            e.Property(x => x.DemoCredits).HasPrecision(28, 6);
            e.HasIndex(x => new { x.TenantId, x.ProjectId, x.StartedAt });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        // Preserve the retired feature's schema so future migrations do not drop saved dashboards.
        b.Entity<PersonalDashboardRow>(e =>
        {
            e.ToTable("personal_dashboards");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ProjectId, x.UserId });
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<ModelConnectionRow>(e =>
        {
            e.ToTable("model_connections");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<BuildConversationRow>(e =>
        {
            e.ToTable("build_conversations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.ProjectId, x.UpdatedAt });
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.Status, x.UpdatedAt });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<BuildMemoryRow>(e =>
        {
            e.ToTable("build_memories");
            e.Property(x => x.Settings).HasDefaultValue("");
            e.HasKey(x => new { x.TenantId, x.UserId, x.ProjectId });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<WorkflowDemoShareRow>(e =>
        {
            e.ToTable("workflow_demo_shares");
            e.HasKey(x => new { x.TenantId, x.UserId, x.RunId });
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

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
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.PasswordSet).HasDefaultValue(false);
            e.Property(x => x.IsDefaultAdmin).HasDefaultValue(false);
            e.Property(x => x.IsExternalAdmin).HasDefaultValue(false);
            e.Property(x => x.TwoFactorEnabled).HasDefaultValue(false);
            e.Property(x => x.TwoFactorChannel).HasDefaultValue("email");
            e.Property(x => x.TwoFactorCodeFailedAttempts).HasDefaultValue(0);
        });

        b.Entity<InviteRow>(e =>
        {
            e.ToTable("invites");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<UserApiTokenRow>(e =>
        {
            e.ToTable("user_api_tokens");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<UserPermissionGrantRow>(e =>
        {
            e.ToTable("user_permission_grants");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.Permission }).IsUnique(); // one override per (user, permission)
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<RoleDefinitionRow>(e =>
        {
            e.ToTable("role_definitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); // role name unique within a tenant
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.IsSystem).HasDefaultValue(false);
            e.Property(x => x.PermissionsJson).HasDefaultValue("[]");
        });

        b.Entity<ProjectRow>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Path }).IsUnique(); // path unique within a tenant
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<ActivityRecordRow>(e =>
        {
            e.ToTable("activity_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Sequence });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<DecisionRow>(e =>
        {
            e.ToTable("decisions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<RequirementsRow>(e =>
        {
            e.ToTable("requirements");
            // Composite key: each tenant has its own per-project docs AND its own global doc (ProjectId = Guid.Empty).
            e.HasKey(x => new { x.TenantId, x.ProjectId });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<UsageRow>(e =>
        {
            e.ToTable("usage_records");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.RecordedAt });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<DataSourceRow>(e =>
        {
            e.ToTable("data_sources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.ProjectId });
            e.HasOne<ProjectRow>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });
        b.Entity<SourceDocumentRow>(e =>
        {
            e.ToTable("source_documents");
            e.HasKey(x => new { x.SourceId, x.KeyHash });
            e.HasOne<DataSourceRow>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<ToolCallRow>(e =>
        {
            e.ToTable("tool_calls");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.At);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobRow>(e =>
        {
            e.ToTable("jobs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            // New columns added for WorkloadSource discriminated union.
            e.Property(x => x.MapSourceKind).HasDefaultValue("image");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.AllowNetworkEgress).HasDefaultValue(false);
            e.Property(x => x.AllowApiInvocation).HasDefaultValue(false);
            e.Property(x => x.TimeoutSeconds).HasDefaultValue(1800);
            e.Property(x => x.ParametersJson).HasDefaultValue("[]");
            e.Property(x => x.PostJobActionsJson).HasDefaultValue("[]");
            e.Property(x => x.ReturnType).HasDefaultValue("Json");
            e.Property(x => x.RetryCount).HasDefaultValue(0);
            e.Property(x => x.RetryDelaySeconds).HasDefaultValue(0);
        });

        b.Entity<JobTestCaseRow>(e =>
        {
            e.ToTable("job_test_cases");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.JobId });
            e.HasIndex(x => new { x.JobId, x.Name }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.AssertionType).HasDefaultValue("Succeeds");
            e.Property(x => x.Enabled).HasDefaultValue(true);
            e.Property(x => x.LastStatus).HasDefaultValue("NotRun");
            e.Property(x => x.CodeFilesJson).HasDefaultValue("[]");
            e.Property(x => x.MethodResultsJson).HasDefaultValue("[]");
            e.Property(x => x.AllowNetworkEgress).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.HasOne<JobRow>()
                .WithMany()
                .HasForeignKey(x => x.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SearchDashboardRow>(e =>
        {
            e.ToTable("opensearch_dashboards");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.IndexPattern).HasDefaultValue("*");
            e.Property(x => x.BucketType).HasDefaultValue("terms");
            e.Property(x => x.ChartType).HasDefaultValue("bar");
            e.Property(x => x.MetricType).HasDefaultValue("count");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<SavedQueryRow>(e =>
        {
            e.ToTable("saved_queries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<CommunicationProviderRow>(e =>
        {
            e.ToTable("communication_providers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Channel });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
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

        b.Entity<RunArtifactLinkRow>(e =>
        {
            e.ToTable("job_run_artifacts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RunId);
            // Partial index over the "still pending OCR" set — keeps the daemon's oldest-first
            // drain query fast as artifact count grows.
            e.HasIndex(x => x.OcrProcessedAt)
                .HasFilter("\"OcrProcessedAt\" IS NULL")
                .HasDatabaseName("ix_job_run_artifacts_ocr");
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        b.Entity<ArtifactShareTokenRow>(e =>
        {
            e.ToTable("artifact_share_tokens");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ArtifactId).IsUnique();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.TokenPrefix).HasMaxLength(20);
            e.HasOne<RunArtifactLinkRow>()
                .WithMany()
                .HasForeignKey(x => x.ArtifactId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProjectChartRow>(e =>
        {
            e.ToTable("project_charts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.TableName }).IsUnique();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<EntityTagRow>(e =>
        {
            e.ToTable("entity_tags");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityId, x.Key });
            e.HasIndex(x => x.RunId);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<RecordLinkRow>(e =>
        {
            e.ToTable("record_links");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.NormalizedValue });
            e.HasIndex(x => new { x.ProjectId, x.TableName, x.RowKey });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<DataEntityRow>(e =>
        {
            e.ToTable("data_entities");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.RelationsJson).HasDefaultValue("[]");
            e.Property(x => x.TagsJson).HasDefaultValue("[]");
        });

        b.Entity<DataMappingRow>(e =>
        {
            e.ToTable("data_mappings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.JobId);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
            e.Property(x => x.FieldsJson).HasDefaultValue("[]");
            e.Property(x => x.Enabled).HasDefaultValue(true);
            e.Property(x => x.SourceKind).HasDefaultValue("job");
        });

        b.Entity<JobSecretRow>(e =>
        {
            e.ToTable("job_secrets");
            e.HasKey(x => new { x.ProjectId, x.Name });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobRunRow>(e =>
        {
            e.ToTable("job_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsConcurrencyToken();
            e.HasIndex(x => new { x.JobId, x.StartedAt });
            e.HasIndex(x => new { x.TenantId, x.StartedAt }).HasDatabaseName("ix_job_runs_tenant_started");
            e.HasIndex(x => x.ProjectId);
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
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
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<JobChainRow>(e =>
        {
            e.ToTable("job_chains");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ProjectId);
            // Atomic compare-and-swap at SaveChanges: a competing edit that commits
            // after our read cannot silently replace the workflow definition.
            e.Property(x => x.UpdatedAt).IsConcurrencyToken();
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<ChainRunRow>(e =>
        {
            e.ToTable("chain_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsConcurrencyToken();
            // The pipeline history is read newest-first per chain.
            e.HasIndex(x => new { x.ChainId, x.StartedAt });
            e.HasIndex(x => new { x.TenantId, x.StartedAt }).HasDatabaseName("ix_chain_runs_tenant_started");
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => new { x.Status, x.ResumeAt, x.ContinuationClaimedAt });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<EventDefinitionRow>(e =>
        {
            e.ToTable("event_definitions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); // event name unique within a tenant
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<EventOccurrenceRow>(e =>
        {
            e.ToTable("event_occurrences");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Name, x.OccurredAt });
            e.HasQueryFilter(x => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && x.TenantId == _tenant.TenantId);
        });

        b.Entity<PendingRunRow>(e =>
        {
            e.ToTable("pending_job_runs"); // global system queue — NOT tenant-filtered
            e.HasKey(x => x.Id);
            // Drained oldest-first among unclaimed rows.
            e.HasIndex(x => new { x.ClaimedAt, x.EnqueuedAt });
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
            e.HasQueryFilter(r => _tenant.IsResolved && _tenant.TenantId != Guid.Empty && r.TenantId == _tenant.TenantId);
        });

        // Include ownership in UPDATE/DELETE predicates, including detached entities attached by ID.
        foreach (var entity in b.Model.GetEntityTypes().Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)))
        {
            b.Entity(entity.ClrType).Property(nameof(ITenantOwned.TenantId)).IsConcurrencyToken();
        }
    }
}
