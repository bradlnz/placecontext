namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF row for a <see cref="PlaceContext.Domain.Entities.SmsMessage"/> (table sms_messages). From/Body columns hold ciphertext.</summary>
public sealed class SmsMessageRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProjectId { get; set; }
    public string FromProtected { get; set; } = "";
    public string To { get; set; } = "";
    public string BodyProtected { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? ExternalId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
