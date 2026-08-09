namespace PlaceContext.Crm.Integration;

public interface ICrmCommunicationsClient
{
    Task<CrmCommunicationCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);

    Task<CrmMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<CrmEmailAttachment>? attachments = null);

    Task<CrmMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default);
}
