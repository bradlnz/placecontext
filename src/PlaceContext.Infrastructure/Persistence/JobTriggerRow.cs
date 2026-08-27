namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.JobTrigger"/>.</summary>
public sealed class JobTriggerRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public Guid? JobId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>"Schedule" | "Event".</summary>
    public string Kind { get; set; } = "Schedule";
    public bool Enabled { get; set; } = true;

    /// <summary>Cron expression (schedule triggers).</summary>
    public string? CronExpression { get; set; }
    /// <summary>Subscribed event name (event triggers).</summary>
    public string? EventName { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
