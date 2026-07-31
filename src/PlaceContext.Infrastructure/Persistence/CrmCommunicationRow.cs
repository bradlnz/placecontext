namespace PlaceContext.Infrastructure.Persistence;

public sealed class CrmCommunicationRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ClientId { get; set; }
    public string Channel { get; set; } = "";
    public string? SubjectProtected { get; set; }
    public string BodyProtected { get; set; } = "";
    public string? RecipientProtected { get; set; }
    public string Status { get; set; } = "";
    public string? Provider { get; set; }
    public string? ExternalId { get; set; }
    public string? ErrorProtected { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
