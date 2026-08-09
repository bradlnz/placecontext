namespace PlaceContext.Application.Ports;

public interface IClientCommunicationSender
{
    string EmailProvider { get; }
    string SmsProvider { get; }
    Task<IReadOnlyList<string>> TwoFactorChannelsAsync(CancellationToken ct = default);
    Task<ClientCommsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);
    Task<ClientMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<ClientEmailAttachment>? attachments = null);
    Task<ClientMessageDelivery> SendAuthenticationEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default);
    Task<ClientMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default);
    Task<ClientMessageDelivery> SendAuthenticationSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default);
}
