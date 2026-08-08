namespace PlaceContext.Jobs.Infrastructure.Persistence;

/// <summary>Flat EF Core row for an <see cref="PlaceContext.Domain.Entities.EventOccurrence"/> — the event log.</summary>
public sealed class EventOccurrenceRow : IJobsTenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>"User" | "Domain".</summary>
    public string Source { get; set; } = "User";
    public Guid? ProjectId { get; set; }
    public string? Payload { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
