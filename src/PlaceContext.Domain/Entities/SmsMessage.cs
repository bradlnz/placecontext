namespace PlaceContext.Domain.Entities;

/// <summary>
/// One inbound SMS received by the platform's gateway. The sender number and the message body are
/// held ONLY as ciphertext (Data Protection, same scheme as job secrets) — this entity never sees
/// the plaintext, so nothing below the Application layer can leak it. The receiving number and
/// provider are routing metadata and stay plain.
/// </summary>
public sealed class SmsMessage
{
    private SmsMessage(Guid id, Guid? projectId, string fromProtected, string to, string bodyProtected,
        string provider, string? externalId, DateTimeOffset receivedAt)
    {
        Id = id;
        ProjectId = projectId;
        FromProtected = fromProtected;
        To = to;
        BodyProtected = bodyProtected;
        Provider = provider;
        ExternalId = externalId;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; }
    public Guid? ProjectId { get; }

    /// <summary>The sender's number, encrypted at rest.</summary>
    public string FromProtected { get; }

    /// <summary>The platform number that received the message (routing metadata, plain).</summary>
    public string To { get; }

    /// <summary>The message text, encrypted at rest.</summary>
    public string BodyProtected { get; }

    /// <summary>A short lower-case name for the delivering gateway; "generic" when unknown.</summary>
    public string Provider { get; }

    /// <summary>The delivering gateway's own message id, for dedupe and tracing.</summary>
    public string? ExternalId { get; }

    public DateTimeOffset ReceivedAt { get; }

    public static SmsMessage Receive(Guid? projectId, string fromProtected, string to, string bodyProtected,
        string provider, string? externalId, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(fromProtected)) throw new ArgumentException("Sender ciphertext is required.", nameof(fromProtected));
        if (string.IsNullOrWhiteSpace(bodyProtected)) throw new ArgumentException("Body ciphertext is required.", nameof(bodyProtected));
        return new(Guid.NewGuid(), projectId, fromProtected, to, bodyProtected,
            string.IsNullOrWhiteSpace(provider) ? "generic" : provider, externalId, receivedAt);
    }

    public static SmsMessage Rehydrate(Guid id, Guid? projectId, string fromProtected, string to,
        string bodyProtected, string provider, string? externalId, DateTimeOffset receivedAt)
        => new(id, projectId, fromProtected, to, bodyProtected, provider, externalId, receivedAt);
}
