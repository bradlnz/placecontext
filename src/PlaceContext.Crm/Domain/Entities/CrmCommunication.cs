using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>An internal note or outbound message in a client's communication timeline.</summary>
public sealed class CrmCommunication
{
    private CrmCommunication(
        Guid id,
        Guid projectId,
        Guid clientId,
        CrmCommunicationChannel channel,
        string? subject,
        string body,
        string? recipient,
        CrmCommunicationStatus status,
        string? provider,
        string? externalId,
        string? error,
        Guid createdByUserId,
        DateTimeOffset createdAt,
        DateTimeOffset? sentAt)
    {
        Id = id;
        ProjectId = projectId;
        ClientId = clientId;
        Channel = channel;
        Subject = subject;
        Body = body;
        Recipient = recipient;
        Status = status;
        Provider = provider;
        ExternalId = externalId;
        Error = error;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        SentAt = sentAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid ClientId { get; }
    public CrmCommunicationChannel Channel { get; }
    public string? Subject { get; }
    public string Body { get; }
    public string? Recipient { get; }
    public CrmCommunicationStatus Status { get; private set; }
    public string? Provider { get; private set; }
    public string? ExternalId { get; private set; }
    public string? Error { get; private set; }
    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? SentAt { get; private set; }

    public static CrmCommunication CreateNote(
        Guid projectId,
        Guid clientId,
        string body,
        Guid createdByUserId,
        DateTimeOffset now)
        => Create(projectId, clientId, CrmCommunicationChannel.Note, null, body, null,
            CrmCommunicationStatus.Added, createdByUserId, now);

    public static CrmCommunication CreateOutbound(
        Guid projectId,
        Guid clientId,
        CrmCommunicationChannel channel,
        string? subject,
        string body,
        string recipient,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        if (channel is not (CrmCommunicationChannel.Email or CrmCommunicationChannel.Sms))
            throw new ArgumentException("Outbound messages must use email or SMS.", nameof(channel));
        if (channel == CrmCommunicationChannel.Email && string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Email needs a subject.", nameof(subject));
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("The client does not have a recipient for this channel.", nameof(recipient));
        return Create(projectId, clientId, channel, subject, body, recipient,
            CrmCommunicationStatus.Pending, createdByUserId, now);
    }

    public static CrmCommunication Rehydrate(
        Guid id, Guid projectId, Guid clientId, CrmCommunicationChannel channel,
        string? subject, string body, string? recipient, CrmCommunicationStatus status,
        string? provider, string? externalId, string? error, Guid createdByUserId,
        DateTimeOffset createdAt, DateTimeOffset? sentAt)
        => new(id, projectId, clientId, channel, subject, body, recipient, status,
            provider, externalId, error, createdByUserId, createdAt, sentAt);

    public void MarkSent(string provider, string? externalId, DateTimeOffset sentAt)
    {
        Status = CrmCommunicationStatus.Sent;
        Provider = Clean(provider);
        ExternalId = Clean(externalId);
        Error = null;
        SentAt = sentAt;
    }

    public void MarkFailed(string provider, string error)
    {
        Status = CrmCommunicationStatus.Failed;
        Provider = Clean(provider);
        Error = Clean(error) ?? "Message delivery failed.";
        SentAt = null;
    }

    private static CrmCommunication Create(
        Guid projectId, Guid clientId, CrmCommunicationChannel channel, string? subject,
        string body, string? recipient, CrmCommunicationStatus status,
        Guid createdByUserId, DateTimeOffset now)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (clientId == Guid.Empty) throw new ArgumentException("ClientId must not be empty.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Message cannot be empty.", nameof(body));
        return new CrmCommunication(Guid.NewGuid(), projectId, clientId, channel, Clean(subject),
            body.Trim(), Clean(recipient), status, null, null, null, createdByUserId, now, null);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
