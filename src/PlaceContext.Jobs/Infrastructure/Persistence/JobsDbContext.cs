using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Jobs.Domain.Persistence;

namespace PlaceContext.Jobs.Infrastructure.Persistence;

/// <summary>Jobs-owned persistence boundary for jobs, chains, schedules, events, and their runs.</summary>
public sealed class JobsDbContext : DbContext, IJobsUnitOfWork
{
    private readonly ICurrentTenant _currentTenant;

    public JobsDbContext(DbContextOptions<JobsDbContext> options, ICurrentTenant currentTenant)
        : base(options)
        => _currentTenant = currentTenant;

    public DbSet<JobRow> Jobs => Set<JobRow>();
    public DbSet<JobRunRow> JobRuns => Set<JobRunRow>();
    public DbSet<JobTestCaseRow> JobTestCases => Set<JobTestCaseRow>();
    public DbSet<JobTriggerRow> JobTriggers => Set<JobTriggerRow>();
    public DbSet<JobChainRow> JobChains => Set<JobChainRow>();
    public DbSet<ChainRunRow> ChainRuns => Set<ChainRunRow>();
    public DbSet<EventDefinitionRow> EventDefinitions => Set<EventDefinitionRow>();
    public DbSet<EventOccurrenceRow> EventOccurrences => Set<EventOccurrenceRow>();
    public DbSet<PendingRunRow> PendingRuns => Set<PendingRunRow>();

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
        modelBuilder.Entity<JobRow>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ProjectId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.MapSourceKind).HasDefaultValue("image");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.AllowNetworkEgress).HasDefaultValue(false);
            entity.Property(row => row.AllowApiInvocation).HasDefaultValue(false);
            entity.Property(row => row.TimeoutSeconds).HasDefaultValue(1800);
            entity.Property(row => row.ParametersJson).HasDefaultValue("[]");
            entity.Property(row => row.PostJobActionsJson).HasDefaultValue("[]");
            entity.Property(row => row.ReturnType).HasDefaultValue("Json");
            entity.Property(row => row.RetryCount).HasDefaultValue(0);
            entity.Property(row => row.RetryDelaySeconds).HasDefaultValue(0);
        });

        modelBuilder.Entity<JobTestCaseRow>(entity =>
        {
            entity.ToTable("job_test_cases");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ProjectId, row.JobId });
            entity.HasIndex(row => new { row.JobId, row.Name }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.AssertionType).HasDefaultValue("Succeeds");
            entity.Property(row => row.Enabled).HasDefaultValue(true);
            entity.Property(row => row.LastStatus).HasDefaultValue("NotRun");
            entity.Property(row => row.CodeFilesJson).HasDefaultValue("[]");
            entity.Property(row => row.MethodResultsJson).HasDefaultValue("[]");
            entity.Property(row => row.AllowNetworkEgress).HasDefaultValue(false);
            entity.Property(row => row.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(row => row.UpdatedAt).HasDefaultValueSql("now()");
            entity.HasOne<JobRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobRunRow>(entity =>
        {
            entity.ToTable("job_runs");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.JobId, row.StartedAt });
            entity.HasIndex(row => row.ProjectId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
            entity.Property(row => row.SnapshotJson).HasDefaultValue("{}");
            entity.Property(row => row.AttemptNumber).HasDefaultValue(1);
        });

        modelBuilder.Entity<JobTriggerRow>(entity =>
        {
            entity.ToTable("job_triggers");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.JobId).IsRequired(false);
            entity.HasIndex(row => row.JobId);
            entity.HasIndex(row => new { row.Enabled, row.Kind, row.NextRunAt });
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<JobChainRow>(entity =>
        {
            entity.ToTable("job_chains");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.ProjectId);
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<ChainRunRow>(entity =>
        {
            entity.ToTable("chain_runs");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ChainId, row.StartedAt });
            entity.HasIndex(row => row.ProjectId);
            entity.HasIndex(row => new { row.Status, row.ResumeAt, row.ContinuationClaimedAt });
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<EventDefinitionRow>(entity =>
        {
            entity.ToTable("event_definitions");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.TenantId, row.Name }).IsUnique();
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<EventOccurrenceRow>(entity =>
        {
            entity.ToTable("event_occurrences");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.Name, row.OccurredAt });
            entity.HasQueryFilter(row => row.TenantId == _currentTenant.TenantId);
        });

        modelBuilder.Entity<PendingRunRow>(entity =>
        {
            entity.ToTable("pending_job_runs");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.ClaimedAt, row.EnqueuedAt });
        });
    }

    private void StampTenant()
    {
        var tenantId = _currentTenant.TenantId;
        foreach (var entry in ChangeTracker.Entries<IJobsTenantOwned>())
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;
    }
}
