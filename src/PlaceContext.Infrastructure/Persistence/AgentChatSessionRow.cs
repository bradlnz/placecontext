namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.AgentChatSession"/>.
/// Messages are stored as a JSONB text column for Phase 1 simplicity.
/// </summary>
public sealed class AgentChatSessionRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? UserId { get; set; }
    public string? Title { get; set; }
    /// <summary>JSON array of {Role, Content, Timestamp} messages.</summary>
    public string MessagesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
