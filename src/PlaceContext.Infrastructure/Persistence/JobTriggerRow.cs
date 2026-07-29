namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.JobTrigger"/>.</summary>
public sealed class JobTriggerRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Null for launchpads (they target a chain, not a job).</summary>
    public Guid? JobId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>"Schedule" | "Event" | "Launchpad".</summary>
    public string Kind { get; set; } = "Schedule";
    public bool Enabled { get; set; } = true;

    /// <summary>Cron expression (schedule and launchpad triggers).</summary>
    public string? CronExpression { get; set; }
    /// <summary>Subscribed event name (event triggers).</summary>
    public string? EventName { get; set; }

    /// <summary>Job chain the launchpad agent session targets (launchpads only).</summary>
    public Guid? ChainId { get; set; }
    /// <summary>Project data table fetched into the launchpad session context (launchpads only).</summary>
    public string? SourceTable { get; set; }
    /// <summary>Prompt the launchpad session runs autonomously (launchpads only).</summary>
    public string? Prompt { get; set; }

    /// <summary>Optional command executed when this trigger fires (command triggers).</summary>
    public Guid? CommandId { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
